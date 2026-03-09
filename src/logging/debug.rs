#![allow(unused_variables)]

use std::ffi::CString;
use std::os::raw::c_char;
use std::sync::{Mutex, OnceLock};

#[cfg(windows)]
pub type DebugCallback = extern "system" fn(*const c_char);
#[cfg(not(windows))]
pub type DebugCallback = extern "C" fn(*const c_char);

struct DebugState {
    log_callback: Option<DebugCallback>,
    warning_callback: Option<DebugCallback>,
    error_callback: Option<DebugCallback>,
    queued_logs: Vec<String>,
    queued_warnings: Vec<String>,
    queued_errors: Vec<String>,
    cache_logs: bool,
}

impl Default for DebugState {
    fn default() -> Self {
        Self {
            log_callback: None,
            warning_callback: None,
            error_callback: None,
            queued_logs: Vec::new(),
            queued_warnings: Vec::new(),
            queued_errors: Vec::new(),
            cache_logs: true,
        }
    }
}

static DEBUG_STATE: OnceLock<Mutex<DebugState>> = OnceLock::new();

fn state() -> &'static Mutex<DebugState> {
    DEBUG_STATE.get_or_init(|| Mutex::new(DebugState::default()))
}

pub fn initialize(cache_logs: bool) {
    let mut s = state().lock().unwrap_or_else(|p| p.into_inner());
    s.cache_logs = cache_logs;
}

#[export_name = "RustAV_DebugInitialize"]
pub extern "system" fn rustav_debug_initialize(cache_logs: bool) {
    initialize(cache_logs);
}

pub fn teardown() {
    let mut s = state().lock().unwrap_or_else(|p| p.into_inner());
    s.log_callback = None;
    s.warning_callback = None;
    s.error_callback = None;
    s.queued_logs.clear();
    s.queued_warnings.clear();
    s.queued_errors.clear();
}

#[export_name = "RustAV_DebugTeardown"]
pub extern "system" fn rustav_debug_teardown() {
    teardown();
}

pub fn deregister_all_callbacks() {
    let mut s = state().lock().unwrap_or_else(|p| p.into_inner());
    s.log_callback = None;
    s.warning_callback = None;
    s.error_callback = None;
}

#[export_name = "RustAV_DebugClearCallbacks"]
pub extern "system" fn rustav_debug_clear_callbacks() {
    deregister_all_callbacks();
}

pub fn register_log_callback(callback: DebugCallback) {
    let mut s = state().lock().unwrap_or_else(|p| p.into_inner());

    if callback as usize == 0 {
        return;
    }

    s.log_callback = Some(callback);

    if let Some(cb) = s.log_callback {
        for msg in s.queued_logs.drain(..) {
            let flush_msg = format!("flush: {}", msg);
            if let Ok(c_msg) = CString::new(flush_msg) {
                cb(c_msg.as_ptr());
            }
        }
    }
}

#[export_name = "RustAV_DebugRegisterLogCallback"]
pub extern "system" fn rustav_debug_register_log_callback(callback: DebugCallback) {
    register_log_callback(callback);
}

pub fn register_warning_callback(callback: DebugCallback) {
    let mut s = state().lock().unwrap_or_else(|p| p.into_inner());

    if callback as usize == 0 {
        return;
    }

    s.warning_callback = Some(callback);

    if let Some(cb) = s.warning_callback {
        for msg in s.queued_warnings.drain(..) {
            let flush_msg = format!("flush: {}", msg);
            if let Ok(c_msg) = CString::new(flush_msg) {
                cb(c_msg.as_ptr());
            }
        }
    }
}

#[export_name = "RustAV_DebugRegisterWarningCallback"]
pub extern "system" fn rustav_debug_register_warning_callback(callback: DebugCallback) {
    register_warning_callback(callback);
}

pub fn register_error_callback(callback: DebugCallback) {
    let mut s = state().lock().unwrap_or_else(|p| p.into_inner());

    if callback as usize == 0 {
        return;
    }

    s.error_callback = Some(callback);

    if let Some(cb) = s.error_callback {
        for msg in s.queued_errors.drain(..) {
            let flush_msg = format!("flush: {}", msg);
            if let Ok(c_msg) = CString::new(flush_msg) {
                cb(c_msg.as_ptr());
            }
        }
    }
}

#[export_name = "RustAV_DebugRegisterErrorCallback"]
pub extern "system" fn rustav_debug_register_error_callback(callback: DebugCallback) {
    register_error_callback(callback);
}

pub struct Debug;

impl Debug {
    pub fn log(msg: &str) {
        let cb = {
            let mut s = state().lock().unwrap_or_else(|p| p.into_inner());
            if s.log_callback.is_none() && s.cache_logs {
                s.queued_logs.push(msg.to_string());
            }
            s.log_callback
        };

        if let Some(cb) = cb {
            if let Ok(c_msg) = CString::new(msg) {
                cb(c_msg.as_ptr());
            }
        }

        println!("{}", msg);
    }

    pub fn log_warning(msg: &str) {
        let cb = {
            let mut s = state().lock().unwrap_or_else(|p| p.into_inner());
            if s.warning_callback.is_none() && s.cache_logs {
                s.queued_warnings.push(msg.to_string());
            }
            s.warning_callback
        };

        if let Some(cb) = cb {
            if let Ok(c_msg) = CString::new(msg) {
                cb(c_msg.as_ptr());
            }
        }

        println!("{}", msg);
    }

    pub fn log_error(msg: &str) {
        let cb = {
            let mut s = state().lock().unwrap_or_else(|p| p.into_inner());
            if s.error_callback.is_none() && s.cache_logs {
                s.queued_errors.push(msg.to_string());
            }
            s.error_callback
        };

        if let Some(cb) = cb {
            if let Ok(c_msg) = CString::new(msg) {
                cb(c_msg.as_ptr());
            }
        }

        eprintln!("{}", msg);
    }
}
