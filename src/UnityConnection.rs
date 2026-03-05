#![allow(non_snake_case)]
#![allow(non_camel_case_types)]

use crate::dllmain::UnityInterfacesReady;
use crate::Player::Player;
use lazy_static::lazy_static;
use std::ffi::CStr;
use std::os::raw::{c_char, c_double, c_int, c_void};
use std::sync::{Arc, Mutex};

lazy_static! {
    pub static ref gPlayers: Mutex<Vec<Option<Arc<Mutex<Player>>>>> = Mutex::new(Vec::new());
}

#[no_mangle]
pub extern "system" fn GetPlayer(path: *const c_char, targetTexture: *mut c_void) -> c_int {
    let result = -1;

    if path.is_null() || targetTexture.is_null() {
        return result;
    }

    if !UnityInterfacesReady() {
        return result;
    }

    let path_str = unsafe { CStr::from_ptr(path) }
        .to_string_lossy()
        .into_owned();

    TryCleanPlayersCache();

    let player = Player::CreateWithTexture(path_str, targetTexture);

    let mut players = match gPlayers.lock() {
        Ok(p) => p,
        Err(poisoned) => poisoned.into_inner(),
    };

    players.push(player.map(|p| Arc::new(Mutex::new(p))));
    (players.len() - 1) as c_int
}

pub fn ForcePlayersWrite() {
    let players = match gPlayers.lock() {
        Ok(p) => p,
        Err(poisoned) => poisoned.into_inner(),
    };

    let snapshot: Vec<Arc<Mutex<Player>>> =
        players.iter().filter_map(|p| p.as_ref().cloned()).collect();
    drop(players);

    for player in snapshot {
        match player.lock() {
            Ok(mut guard) => guard.Write(),
            Err(poisoned) => poisoned.into_inner().Write(),
        }
    }
}

#[no_mangle]
pub extern "system" fn ReleasePlayer(id: c_int) -> c_int {
    if id < 0 {
        return -1;
    }

    let released = {
        let mut players = gPlayers.lock().unwrap_or_else(|p| p.into_inner());
        let idx = id as usize;
        if idx >= players.len() {
            return -1;
        }

        players[idx].take()
    };

    if released.is_some() {
        1
    } else {
        -1
    }
}

#[no_mangle]
pub extern "system" fn ForcePlayerWrite(id: c_int) {
    with_player_mut(id, (), |player| {
        player.Write();
    });
}

#[no_mangle]
pub extern "system" fn Duration(id: c_int) -> c_double {
    with_player(id, -1.0, |player| player.Duration())
}

#[no_mangle]
pub extern "system" fn Time(id: c_int) -> c_double {
    with_player(id, -1.0, |player| player.CurrentTime())
}

#[no_mangle]
pub extern "system" fn Play(id: c_int) -> c_int {
    with_player(id, -1, |player| {
        player.Play();
        0
    })
}

#[no_mangle]
pub extern "system" fn Stop(id: c_int) -> c_int {
    with_player(id, -1, |player| {
        player.Stop();
        0
    })
}

#[no_mangle]
pub extern "system" fn Seek(id: c_int, time: c_double) -> c_double {
    with_player(id, -1.0, |player| {
        player.Seek(time);
        0.0
    })
}

#[no_mangle]
pub extern "system" fn SetLoop(id: c_int, loop_value: bool) -> c_int {
    with_player(id, -1, |player| {
        player.SetLoop(loop_value);
        0
    })
}

pub fn ValidatePlayerId(id: c_int) -> bool {
    if id < 0 {
        return false;
    }

    let players = gPlayers.lock().unwrap_or_else(|p| p.into_inner());
    if id as usize >= players.len() {
        return false;
    }

    players[id as usize].is_some()
}

fn with_player<T, F>(id: c_int, default: T, f: F) -> T
where
    F: FnOnce(&Player) -> T,
{
    let player = match snapshot_player(id) {
        Some(p) => p,
        None => return default,
    };

    let result = match player.lock() {
        Ok(guard) => f(&guard),
        Err(poisoned) => {
            let guard = poisoned.into_inner();
            f(&guard)
        }
    };
    result
}

fn with_player_mut<T, F>(id: c_int, default: T, f: F) -> T
where
    F: FnOnce(&mut Player) -> T,
{
    let player = match snapshot_player(id) {
        Some(p) => p,
        None => return default,
    };

    let result = match player.lock() {
        Ok(mut guard) => f(&mut guard),
        Err(poisoned) => {
            let mut guard = poisoned.into_inner();
            f(&mut guard)
        }
    };
    result
}

fn snapshot_player(id: c_int) -> Option<Arc<Mutex<Player>>> {
    if id < 0 {
        return None;
    }

    let players = gPlayers.lock().unwrap_or_else(|p| p.into_inner());
    let idx = id as usize;
    if idx >= players.len() {
        return None;
    }

    players[idx].as_ref().cloned()
}

pub fn TryCleanPlayersCache() {
    let mut players = gPlayers.lock().unwrap_or_else(|p| p.into_inner());

    if players.is_empty() {
        return;
    }

    let any_enabled = players.iter().any(|p| p.is_some());
    if !any_enabled {
        players.clear();
    }
}
