using System;
using UnityEngine;
using Nox.Audio;
using Nox.CCK.Events;
using Nox.CCK.Mods.Cores;
using Nox.CCK.Mods.Events;
using UnityEngine.Audio;

namespace Nox.CCK.Audio {
	/// <summary>
	/// Registers an audio channel and protects it from removal.
	/// Subscribes to <c>audio.channel.remove_requested</c> and blocks
	/// removal until <see cref="Dispose"/> is called.
	/// <para>
	/// Usage in <c>nox.relay</c>:
	/// <code>
	/// _voiceChannel = new ChannelRegister("voice", new[]{"general"}, CoreAPI);
	/// </code>
	/// </para>
	/// </summary>
	public sealed class ChannelRegister : IDisposable {
		private readonly string _id;
		private readonly IModCoreAPI _coreAPI;
		private EventSubscription[] _subscriptions = Array.Empty<EventSubscription>();

		/// <summary>Used to hand out a distinct mixer track ("00".."FF") per channel.</summary>
		private static int _nextTrack;

		/// <summary>
		/// The <see cref="AudioMixer"/> asset (loaded from <c>audio:mixer.mixer</c>).
		/// Exposes 256 tracks named "00".."FF" under the Master group.
		/// </summary>
		public AudioMixer Mixer { get; private set; }

		/// <summary>
		/// The <see cref="AudioMixerGroup"/> dedicated to this channel. Assign it to
		/// <see cref="AudioSource.outputAudioMixerGroup"/> of every AudioSource handled
		/// by this channel so they route through a dedicated mixer track.
		/// </summary>
		public AudioMixerGroup MixerGroup { get; private set; }

		/// <summary>Name of this channel's exposed volume parameter on the mixer.</summary>
		public string VolumeParam { get; private set; }

		public ChannelRegister(string id, string[] depends, IModCoreAPI coreAPI) {
			_id      = id;
			_coreAPI = coreAPI;

			var api = coreAPI.ModAPI
				.GetMod("audio")
				?.GetInstance<IAudioAPI>();

			if (api == null)
				throw new InvalidOperationException($"ChannelRegister: Audio API not available, cannot register channel '{id}'.");

            Channel = api.Register(id, depends);

			// Load the mixer asset (256 tracks "00".."FF") and hand this channel a
			// dedicated track so every AudioSource routed to it mixes independently.
			Mixer = coreAPI.AssetAPI.GetAsset<AudioMixer>("audio:mixer.mixer");
			int track = _nextTrack++ & 0xFF;
			MixerGroup = GetTrack(track);
			VolumeParam = $"Volume_{track:X2}";

			_subscriptions = new[] {
				coreAPI.EventAPI.Subscribe("audio.channel.remove_requested", OnRemoveRequested),
				coreAPI.EventAPI.Subscribe("audio.channel.volume_changed", OnVolumeChanged),
				coreAPI.EventAPI.Subscribe("audio.channel.mute_changed", OnMuteChanged)
			};

			// Apply the initial channel volume/mute to the mixer track.
			SetVolume(Channel.IsEffectivelyMuted ? 0f : Channel.EffectiveVolume);
		}

		/// <summary>
		/// Get the <see cref="AudioMixerGroup"/> for a track index (0..255, i.e. "00".."FF").
		/// Returns null if the mixer is unavailable or the index is out of range.
		/// </summary>
		public AudioMixerGroup GetTrack(int index) {
			if (Mixer == null || index < 0 || index > 255)
				return null;
			return Mixer.FindMatchingGroups($"{index:X2}")[0];
		}

		/// <summary>
		/// Set this channel's mixer-track volume. <paramref name="linear"/> is a
		/// [0..1] linear amplitude; 0 mute. Does nothing if no mixer/param available.
		/// </summary>
		public void SetVolume(float linear) {
			if (Mixer == null || string.IsNullOrEmpty(VolumeParam))
				return;

			float decibels = linear > 0f ? 20f * Mathf.Log10(linear) : -80f;
			Mixer.SetFloat(VolumeParam, Mathf.Clamp(decibels, -80f, 0f));
		}


        public IChannelAudio Channel { get; }

		public readonly NoxEvent<float, float> OnVolume = new();

		public readonly NoxEvent<bool, bool> OnMute = new();

        private void OnRemoveRequested(EventData context) {
			if (!context.TryGet(0, out (IChannelAudio, object) tuple))
				return;
			if (tuple.Item1?.Id != _id)
				return;
			if (!context.TryGet(1, out Action<object[]> callback))
				return;

			callback(new object[] { false });
		}

        private void OnMuteChanged(EventData context) {
			if (!context.TryGet(0, out IChannelAudio c) || c.Id != _id)
				return;
			OnMute.Invoke(Channel.IsMuted, Channel.IsEffectivelyMuted);
			SetVolume(Channel.IsEffectivelyMuted ? 0f : Channel.EffectiveVolume);
        }

        private void OnVolumeChanged(EventData context) {
			if (!context.TryGet(0, out IChannelAudio c) || c.Id != _id)
				return;
			OnVolume.Invoke(Channel.Volume, Channel.EffectiveVolume);
			SetVolume(Channel.EffectiveVolume);
        }

		public void Dispose() {
			foreach (var subscription in _subscriptions)
				_coreAPI.EventAPI.Unsubscribe(subscription);
			_subscriptions = Array.Empty<EventSubscription>();
			_coreAPI.ModAPI
				.GetMod("audio")
				?.GetInstance<IAudioAPI>()
				?.UnRegister(_id);
		}
	}
}
