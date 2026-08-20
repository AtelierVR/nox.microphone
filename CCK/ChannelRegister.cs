using System;
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

		/// <summary>
		/// The voice <see cref="AudioMixer"/> asset (loaded from <c>audio:mixer.mixer</c>).
		/// Exposes 256 tracks named "00".."FF" under the Master group.
		/// </summary>
		public AudioMixer Mixer { get; private set; }

		/// <summary>
		/// The Master <see cref="AudioMixerGroup"/> of the mixer. Assign this to an
		/// <see cref="AudioSource.outputAudioMixerGroup"/> to route audio through the mixer.
		/// </summary>
		public AudioMixerGroup MixerGroup {
			get {
				if (Mixer == null)
					return null;
				var groups = Mixer.FindMatchingGroups("Master");
				return groups.Length > 0 ? groups[0] : null;
			}
		}

		public ChannelRegister(string id, string[] depends, IModCoreAPI coreAPI) {
			_id      = id;
			_coreAPI = coreAPI;

			var api = coreAPI.ModAPI
				.GetMod("audio")
				?.GetInstance<IAudioAPI>();

			if (api == null)
				throw new InvalidOperationException($"ChannelRegister: Audio API not available, cannot register channel '{id}'.");

            Channel = api.Register(id, depends);

			// Load the voice mixer asset (256 tracks "00".."FF").
			Mixer = coreAPI.AssetAPI.GetAsset<AudioMixer>("audio:mixer.mixer");

			_subscriptions = new[] {
				coreAPI.EventAPI.Subscribe("audio.channel.remove_requested", OnRemoveRequested),
				coreAPI.EventAPI.Subscribe("audio.channel.volume_changed", OnVolumeChanged),
				coreAPI.EventAPI.Subscribe("audio.channel.mute_changed", OnMuteChanged)
			};
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
        }

        private void OnVolumeChanged(EventData context) {
			if (!context.TryGet(0, out IChannelAudio c) || c.Id != _id)
				return;
			OnVolume.Invoke(Channel.Volume, Channel.EffectiveVolume);
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
