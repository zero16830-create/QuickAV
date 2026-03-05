#![allow(non_snake_case)]

use crate::UnityConnection::{gPlayers, ForcePlayersWrite};
use std::os::raw::{c_int, c_void};
use std::ptr;
use std::sync::atomic::{AtomicPtr, Ordering};

#[repr(C)]
struct UnityInterfaceGUID {
    m_GUIDHigh: u64,
    m_GUIDLow: u64,
}

type UnityGetInterface = unsafe extern "system" fn(UnityInterfaceGUID) -> *mut c_void;
type UnityRegisterInterface = unsafe extern "system" fn(UnityInterfaceGUID, *mut c_void);
type UnityGetInterfaceSplit = unsafe extern "system" fn(u64, u64) -> *mut c_void;
type UnityRegisterInterfaceSplit = unsafe extern "system" fn(u64, u64, *mut c_void);

#[repr(C)]
struct IUnityInterfaces {
    GetInterface: UnityGetInterface,
    RegisterInterface: UnityRegisterInterface,
    GetInterfaceSplit: UnityGetInterfaceSplit,
    RegisterInterfaceSplit: UnityRegisterInterfaceSplit,
}

type IUnityGraphicsDeviceEventCallback = extern "system" fn(c_int);
type UnityGetRenderer = unsafe extern "system" fn() -> c_int;
type UnityRegisterDeviceEventCallback =
    unsafe extern "system" fn(IUnityGraphicsDeviceEventCallback);
type UnityUnregisterDeviceEventCallback =
    unsafe extern "system" fn(IUnityGraphicsDeviceEventCallback);
type UnityReserveEventIDRange = unsafe extern "system" fn(c_int) -> c_int;

#[repr(C)]
struct IUnityGraphics {
    GetRenderer: UnityGetRenderer,
    RegisterDeviceEventCallback: UnityRegisterDeviceEventCallback,
    UnregisterDeviceEventCallback: UnityUnregisterDeviceEventCallback,
    ReserveEventIDRange: UnityReserveEventIDRange,
}

const IUNITY_GRAPHICS_GUID_HIGH: u64 = 0x7CBA0A9CA4DDB544;
const IUNITY_GRAPHICS_GUID_LOW: u64 = 0x8C5AD4926EB17B11;
const K_UNITY_GFX_DEVICE_EVENT_INITIALIZE: c_int = 0;
const K_UNITY_GFX_DEVICE_EVENT_SHUTDOWN: c_int = 1;
static G_UNITY_INTERFACES: AtomicPtr<c_void> = AtomicPtr::new(ptr::null_mut());
static G_UNITY_GRAPHICS: AtomicPtr<c_void> = AtomicPtr::new(ptr::null_mut());
static G_UNITY_RENDERER: std::sync::atomic::AtomicI32 = std::sync::atomic::AtomicI32::new(-1);

pub fn UnityInterfacesReady() -> bool {
    !G_UNITY_INTERFACES.load(Ordering::SeqCst).is_null()
}

pub fn UnityRenderer() -> c_int {
    let cached = G_UNITY_RENDERER.load(Ordering::SeqCst);
    if cached >= 0 {
        return cached;
    }

    let graphics = G_UNITY_GRAPHICS.load(Ordering::SeqCst);
    if graphics.is_null() {
        return cached;
    }

    unsafe {
        let unity_graphics = graphics as *mut IUnityGraphics;
        let renderer = ((*unity_graphics).GetRenderer)();
        G_UNITY_RENDERER.store(renderer, Ordering::SeqCst);
        renderer
    }
}

unsafe fn ResolveUnityGraphics(interfaces: *mut c_void) -> *mut c_void {
    if interfaces.is_null() {
        return ptr::null_mut();
    }

    let unity_interfaces = interfaces as *mut IUnityInterfaces;
    ((*unity_interfaces).GetInterfaceSplit)(IUNITY_GRAPHICS_GUID_HIGH, IUNITY_GRAPHICS_GUID_LOW)
}

#[no_mangle]
pub extern "system" fn OnRenderEvent(_eventId: c_int) {
    ForcePlayersWrite();
}

pub extern "system" fn OnGraphicsDeviceEvent(event_type: c_int) {
    match event_type {
        K_UNITY_GFX_DEVICE_EVENT_INITIALIZE => {
            let graphics = G_UNITY_GRAPHICS.load(Ordering::SeqCst);
            if !graphics.is_null() {
                unsafe {
                    let unity_graphics = graphics as *mut IUnityGraphics;
                    let renderer = ((*unity_graphics).GetRenderer)();
                    G_UNITY_RENDERER.store(renderer, Ordering::SeqCst);
                }
            }
        }
        K_UNITY_GFX_DEVICE_EVENT_SHUTDOWN => {
            G_UNITY_RENDERER.store(-1, Ordering::SeqCst);
        }
        _ => {}
    }
}

#[no_mangle]
pub extern "system" fn UnityPluginLoad(interfaces: *mut c_void) {
    G_UNITY_INTERFACES.store(interfaces, Ordering::SeqCst);

    let graphics = unsafe { ResolveUnityGraphics(interfaces) };
    G_UNITY_GRAPHICS.store(graphics, Ordering::SeqCst);

    if !graphics.is_null() {
        unsafe {
            let unity_graphics = graphics as *mut IUnityGraphics;
            ((*unity_graphics).RegisterDeviceEventCallback)(OnGraphicsDeviceEvent);
        }
    }

    OnGraphicsDeviceEvent(K_UNITY_GFX_DEVICE_EVENT_INITIALIZE);

    let mut players = gPlayers.lock().unwrap_or_else(|p| p.into_inner());
    if players.is_empty() {
        *players = Vec::new();
    }
}

#[no_mangle]
pub extern "system" fn UnityPluginUnload() {
    G_UNITY_RENDERER.store(-1, Ordering::SeqCst);

    let graphics = G_UNITY_GRAPHICS.swap(ptr::null_mut(), Ordering::SeqCst);

    if !graphics.is_null() {
        unsafe {
            let unity_graphics = graphics as *mut IUnityGraphics;
            ((*unity_graphics).UnregisterDeviceEventCallback)(OnGraphicsDeviceEvent);
        }
    }

    G_UNITY_INTERFACES.store(ptr::null_mut(), Ordering::SeqCst);

    let old_players = {
        let mut players = gPlayers.lock().unwrap_or_else(|p| p.into_inner());
        std::mem::take(&mut *players)
    };
    drop(old_players);
}

#[no_mangle]
pub extern "system" fn GetRenderEventFunc() -> extern "system" fn(c_int) {
    OnRenderEvent
}
