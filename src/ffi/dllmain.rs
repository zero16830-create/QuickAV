use crate::player_registry::{force_players_write, release_all_players, touch_players_registry};
use std::os::raw::{c_int, c_void};
use std::ptr;
use std::sync::atomic::{AtomicPtr, Ordering};

#[repr(C)]
struct UnityInterfaceGUID {
    guid_high: u64,
    guid_low: u64,
}

type UnityGetInterface = unsafe extern "system" fn(UnityInterfaceGUID) -> *mut c_void;
type UnityRegisterInterface = unsafe extern "system" fn(UnityInterfaceGUID, *mut c_void);
type UnityGetInterfaceSplit = unsafe extern "system" fn(u64, u64) -> *mut c_void;
type UnityRegisterInterfaceSplit = unsafe extern "system" fn(u64, u64, *mut c_void);

#[repr(C)]
struct IUnityInterfaces {
    get_interface: UnityGetInterface,
    register_interface: UnityRegisterInterface,
    get_interface_split: UnityGetInterfaceSplit,
    register_interface_split: UnityRegisterInterfaceSplit,
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
    get_renderer: UnityGetRenderer,
    register_device_event_callback: UnityRegisterDeviceEventCallback,
    unregister_device_event_callback: UnityUnregisterDeviceEventCallback,
    reserve_event_id_range: UnityReserveEventIDRange,
}

const IUNITY_GRAPHICS_GUID_HIGH: u64 = 0x7CBA0A9CA4DDB544;
const IUNITY_GRAPHICS_GUID_LOW: u64 = 0x8C5AD4926EB17B11;
const K_UNITY_GFX_DEVICE_EVENT_INITIALIZE: c_int = 0;
const K_UNITY_GFX_DEVICE_EVENT_SHUTDOWN: c_int = 1;
static G_UNITY_INTERFACES: AtomicPtr<c_void> = AtomicPtr::new(ptr::null_mut());
static G_UNITY_GRAPHICS: AtomicPtr<c_void> = AtomicPtr::new(ptr::null_mut());
static G_UNITY_RENDERER: std::sync::atomic::AtomicI32 = std::sync::atomic::AtomicI32::new(-1);

pub fn unity_interfaces_ready() -> bool {
    !G_UNITY_INTERFACES.load(Ordering::SeqCst).is_null()
}

pub fn unity_renderer() -> c_int {
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
        let renderer = ((*unity_graphics).get_renderer)();
        G_UNITY_RENDERER.store(renderer, Ordering::SeqCst);
        renderer
    }
}

unsafe fn resolve_unity_graphics(interfaces: *mut c_void) -> *mut c_void {
    if interfaces.is_null() {
        return ptr::null_mut();
    }

    let unity_interfaces = interfaces as *mut IUnityInterfaces;
    ((*unity_interfaces).get_interface_split)(IUNITY_GRAPHICS_GUID_HIGH, IUNITY_GRAPHICS_GUID_LOW)
}

pub extern "system" fn on_render_event(_event_id: c_int) {
    force_players_write();
}

pub extern "system" fn on_graphics_device_event(event_type: c_int) {
    match event_type {
        K_UNITY_GFX_DEVICE_EVENT_INITIALIZE => {
            let graphics = G_UNITY_GRAPHICS.load(Ordering::SeqCst);
            if !graphics.is_null() {
                unsafe {
                    let unity_graphics = graphics as *mut IUnityGraphics;
                    let renderer = ((*unity_graphics).get_renderer)();
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

#[export_name = "UnityPluginLoad"]
pub extern "system" fn unity_plugin_load(interfaces: *mut c_void) {
    G_UNITY_INTERFACES.store(interfaces, Ordering::SeqCst);

    let graphics = unsafe { resolve_unity_graphics(interfaces) };
    G_UNITY_GRAPHICS.store(graphics, Ordering::SeqCst);

    if !graphics.is_null() {
        unsafe {
            let unity_graphics = graphics as *mut IUnityGraphics;
            ((*unity_graphics).register_device_event_callback)(on_graphics_device_event);
        }
    }

    on_graphics_device_event(K_UNITY_GFX_DEVICE_EVENT_INITIALIZE);

    touch_players_registry();
}

#[export_name = "UnityPluginUnload"]
pub extern "system" fn unity_plugin_unload() {
    G_UNITY_RENDERER.store(-1, Ordering::SeqCst);

    let graphics = G_UNITY_GRAPHICS.swap(ptr::null_mut(), Ordering::SeqCst);

    if !graphics.is_null() {
        unsafe {
            let unity_graphics = graphics as *mut IUnityGraphics;
            ((*unity_graphics).unregister_device_event_callback)(on_graphics_device_event);
        }
    }

    G_UNITY_INTERFACES.store(ptr::null_mut(), Ordering::SeqCst);

    release_all_players();
}

#[export_name = "RustAV_GetRenderEventFunc"]
pub extern "system" fn rustav_get_render_event_func() -> extern "system" fn(c_int) {
    on_render_event
}
