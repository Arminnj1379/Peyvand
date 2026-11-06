import { useCallback, useEffect, useRef, useState } from 'react';
import {
  Room,
  RoomEvent,
  Track,
  type LocalParticipant,
  type RemoteParticipant,
} from 'livekit-client';
import type { CallJoin } from './callTypes';

export interface RoomPeer {
  identity: string;
  name: string;
  isLocal: boolean;
  isSpeaking: boolean;
  isMuted: boolean;
  hasVideo: boolean;
  participant: LocalParticipant | RemoteParticipant;
}

/**
 * Owns the LiveKit Room for one joined call. The backend minted the token;
 * media flows client↔SFU and never touches the API. Mute/camera are local
 * track toggles (no signaling needed); leaving media is a separate REST call
 * made by the overlay after disconnect.
 */
export function useCallRoom(
  join: CallJoin | null,
  video: boolean,
): {
  peers: RoomPeer[];
  connected: boolean;
  muted: boolean;
  cameraOff: boolean;
  error: string | null;
  toggleMute: () => void;
  toggleCamera: () => void;
  disconnect: () => void;
} {
  const [room] = useState(() => new Room({ adaptiveStream: true, dynacast: true }));
  const [peers, setPeers] = useState<RoomPeer[]>([]);
  const [connected, setConnected] = useState(false);
  const [muted, setMuted] = useState(false);
  const [cameraOff, setCameraOff] = useState(!video);
  const [error, setError] = useState<string | null>(null);
  const joinRef = useRef(join);
  joinRef.current = join;
  const videoRef = useRef(video);
  videoRef.current = video;

  const snapshot = useCallback(() => {
    const r = room;
    const list: RoomPeer[] = [];
    const lp = r.localParticipant;
    list.push({
      identity: lp.identity,
      name: (lp.name || lp.identity || 'You').toString(),
      isLocal: true,
      isSpeaking: lp.isSpeaking,
      isMuted: !lp.isMicrophoneEnabled,
      hasVideo: lp.isCameraEnabled,
      participant: lp,
    });
    for (const p of r.remoteParticipants.values()) {
      const audioPub = [...p.audioTrackPublications.values()][0];
      const videoPub = [...p.videoTrackPublications.values()].find(
        (pub) => pub.track && !pub.isMuted,
      );
      list.push({
        identity: p.identity,
        name: (p.name || p.identity || 'Guest').toString(),
        isLocal: false,
        isSpeaking: p.isSpeaking,
        isMuted: audioPub ? audioPub.isMuted : true,
        hasVideo: videoPub !== undefined,
        participant: p,
      });
    }
    setPeers(list);
  }, [room]);

  useEffect(() => {
    const j = joinRef.current;
    if (!j) return;
    let disposed = false;

    room
      .connect(j.url, j.token)
      .then(async () => {
        if (disposed) return;
        setConnected(true);
        try {
          await room.localParticipant.setMicrophoneEnabled(true);
          await room.localParticipant.setCameraEnabled(videoRef.current);
          if (!videoRef.current) setCameraOff(true);
        } catch {
          if (!disposed) setError('Could not access microphone/camera. Check permissions.');
        }
        snapshot();
      })
      .catch(() => {
        if (!disposed) setError('Could not connect media. Check your connection.');
      });

    const refresh = () => {
      if (!disposed) snapshot();
    };
    room
      .on(RoomEvent.ParticipantConnected, refresh)
      .on(RoomEvent.ParticipantDisconnected, refresh)
      .on(RoomEvent.TrackSubscribed, refresh)
      .on(RoomEvent.TrackUnsubscribed, refresh)
      .on(RoomEvent.TrackMuted, refresh)
      .on(RoomEvent.TrackUnmuted, refresh)
      .on(RoomEvent.LocalTrackPublished, refresh)
      .on(RoomEvent.LocalTrackUnpublished, refresh)
      .on(RoomEvent.ActiveSpeakersChanged, refresh);

    return () => {
      disposed = true;
      room.removeAllListeners();
      void room.disconnect();
    };
  }, [room, snapshot, join?.token]);

  const toggleMute = useCallback(() => {
    const next = !muted;
    setMuted(next);
    void room.localParticipant.setMicrophoneEnabled(!next).catch(() => setMuted(!next));
  }, [room, muted]);

  const toggleCamera = useCallback(() => {
    const next = !cameraOff;
    setCameraOff(next);
    void room.localParticipant.setCameraEnabled(!next).catch(() => setCameraOff(!next));
  }, [room, cameraOff]);

  const disconnect = useCallback(() => {
    void room.disconnect();
    setConnected(false);
  }, [room]);

  return { peers, connected, muted, cameraOff, error, toggleMute, toggleCamera, disconnect };
}

/** Binds a participant's camera/mic tracks to media elements. */
export function PeerTile({ peer }: { peer: RoomPeer }): React.ReactElement {
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const audioRef = useRef<HTMLAudioElement | null>(null);

  useEffect(() => {
    const videoEl = videoRef.current;
    const audioEl = audioRef.current;
    const attachAll = () => {
      const videos: MediaStreamTrack[] = [];
      const audios: MediaStreamTrack[] = [];
      for (const pub of peer.participant.videoTrackPublications.values()) {
        if (pub.track && pub.kind === Track.Kind.Video) videos.push(pub.track.mediaStreamTrack);
      }
      for (const pub of peer.participant.audioTrackPublications.values()) {
        if (pub.track && pub.kind === Track.Kind.Audio) audios.push(pub.track.mediaStreamTrack);
      }
      if (videoEl) {
        videoEl.srcObject = videos.length > 0 ? new MediaStream(videos) : null;
      }
      if (audioEl) {
        audioEl.srcObject = audios.length > 0 ? new MediaStream(audios) : null;
        void audioEl.play().catch(() => undefined);
      }
    };
    attachAll();
    // Room-level events (subscribed in useCallRoom) re-render on track
    // changes, which re-runs this effect — no per-participant listeners needed.
    return () => {
      if (videoEl) videoEl.srcObject = null;
      if (audioEl) audioEl.srcObject = null;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [peer.participant, peer.hasVideo, peer.isMuted]);

  const showVideo = peer.hasVideo;

  return (
    <div className={`peer-tile${peer.isSpeaking ? ' speaking' : ''}`}>
      {showVideo ? (
        <video ref={videoRef} autoPlay playsInline muted={peer.isLocal} />
      ) : (
        <div className="peer-avatar" aria-hidden="true">
          {(peer.name || '?').slice(0, 1).toUpperCase()}
          <video ref={videoRef} hidden muted={peer.isLocal} />
        </div>
      )}
      <audio ref={audioRef} autoPlay />
      <div className="peer-label">
        <span>{peer.isLocal ? 'You' : peer.name}</span>
        {peer.isMuted ? <span className="peer-flag" title="Muted">muted</span> : null}
      </div>
    </div>
  );
}
