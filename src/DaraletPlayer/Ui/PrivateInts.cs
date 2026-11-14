using System;
using System.Collections.Generic;
using System.Linq;
using DaraletPlayer.Feed;
using Decal.Adapter;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Some of your int properties, kept up to date: Decal's (CharacterFilter.GetCharProperty, from login) until the server
    /// sends a private update of one (0x02CD, VitaeInfo.ReadPrivateInt), which Decal may not follow, then that. Another
    /// character logging in starts again from Decal's. Used by the Vitae window (the vitae pool and death level) and the
    /// Vendor window (your pyreals, CoinValue).
    /// </summary>
    internal sealed class PrivateInts : IDisposable {
        private readonly HashSet<uint> keys;
        private readonly Dictionary<uint, int> sent = new Dictionary<uint, int>();
        private readonly string name;
        private int sentFor;

        public PrivateInts(string name, params uint[] keys) {
            this.name = name;
            this.keys = new HashSet<uint>(keys);
            CoreManager.Current.EchoFilter.ServerDispatch += EchoFilter_ServerDispatch;
        }

        public int this[uint key] {
            get {
                Forget();
                return sent.TryGetValue(key, out var value) ? value
                    : CoreManager.Current.CharacterFilter.GetCharProperty((int)key);
            }
        }

        private void Forget() {
            if (sentFor != CoreManager.Current.CharacterFilter.Id) {
                sent.Clear();
                sentFor = CoreManager.Current.CharacterFilter.Id;
            }
        }

        private void EchoFilter_ServerDispatch(object sender, NetworkMessageEventArgs e) {
            try {
                if (e.Message.Type != AetheriaFlags.PrivateUpdateInt) {
                    return;
                }

                var update = VitaeInfo.ReadPrivateInt(e.Message.RawData);

                if (update.HasValue && keys.Contains(update.Value.Key)) {
                    Forget();
                    sent[update.Value.Key] = update.Value.Value;
                }
            }
            catch (Exception ex) {
                PluginCore.LogFile($"[{name}] " + ex);
            }
        }

        public void Dispose() {
            CoreManager.Current.EchoFilter.ServerDispatch -= EchoFilter_ServerDispatch;
        }
    }
}
