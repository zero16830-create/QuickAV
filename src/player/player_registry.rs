use crate::audio_export_state::SharedExportedAudioState;
use crate::frame_export_client::SharedExportedFrameState;
use crate::player::Player;
use lazy_static::lazy_static;
use std::os::raw::c_int;
use std::sync::{Arc, Mutex};

struct PlayerEntry {
    player: Arc<Mutex<Player>>,
    frame_export: Option<SharedExportedFrameState>,
    audio_export: Option<SharedExportedAudioState>,
}

lazy_static! {
    static ref G_PLAYERS: Mutex<Vec<Option<Arc<PlayerEntry>>>> = Mutex::new(Vec::new());
}

pub fn store_player(
    player: Player,
    frame_export: Option<SharedExportedFrameState>,
    audio_export: Option<SharedExportedAudioState>,
) -> c_int {
    let mut players = G_PLAYERS
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner());
    let entry = Arc::new(PlayerEntry {
        player: Arc::new(Mutex::new(player)),
        frame_export,
        audio_export,
    });
    players.push(Some(entry));
    (players.len() - 1) as c_int
}

pub fn release_player(id: c_int) -> bool {
    if id < 0 {
        return false;
    }

    let mut players = G_PLAYERS
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner());
    let idx = id as usize;
    if idx >= players.len() {
        return false;
    }

    players[idx].take().is_some()
}

pub fn force_players_write() {
    let players = G_PLAYERS
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner());
    let snapshot: Vec<Arc<Mutex<Player>>> = players
        .iter()
        .filter_map(|entry| {
            entry
                .as_ref()
                .map(|player_entry| player_entry.player.clone())
        })
        .collect();
    drop(players);

    for player in snapshot {
        match player.lock() {
            Ok(mut guard) => guard.write(),
            Err(poisoned) => poisoned.into_inner().write(),
        }
    }
}

pub fn touch_players_registry() {
    drop(
        G_PLAYERS
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner()),
    );
}

pub fn release_all_players() {
    let old_players = {
        let mut players = G_PLAYERS
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        std::mem::take(&mut *players)
    };
    drop(old_players);
}

pub fn validate_player_id(id: c_int) -> bool {
    if id < 0 {
        return false;
    }

    let players = G_PLAYERS
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner());
    let idx = id as usize;
    idx < players.len() && players[idx].is_some()
}

pub fn with_player<T, F>(id: c_int, default: T, f: F) -> T
where
    F: FnOnce(&Player) -> T,
{
    let player = match snapshot_player(id) {
        Some(player) => player,
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

pub fn with_player_mut<T, F>(id: c_int, default: T, f: F) -> T
where
    F: FnOnce(&mut Player) -> T,
{
    let player = match snapshot_player(id) {
        Some(player) => player,
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

pub fn snapshot_frame_export(id: c_int) -> Option<SharedExportedFrameState> {
    snapshot_entry(id).and_then(|entry| entry.frame_export.clone())
}

pub fn snapshot_audio_export(id: c_int) -> Option<SharedExportedAudioState> {
    snapshot_entry(id).and_then(|entry| entry.audio_export.clone())
}

pub fn try_clean_players_cache() {
    let mut players = G_PLAYERS
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner());
    if players.is_empty() {
        return;
    }

    if !players.iter().any(|entry| entry.is_some()) {
        players.clear();
    }
}

fn snapshot_entry(id: c_int) -> Option<Arc<PlayerEntry>> {
    if id < 0 {
        return None;
    }

    let players = G_PLAYERS
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner());
    let idx = id as usize;
    if idx >= players.len() {
        return None;
    }

    players[idx].as_ref().cloned()
}

fn snapshot_player(id: c_int) -> Option<Arc<Mutex<Player>>> {
    snapshot_entry(id).map(|entry| entry.player.clone())
}
