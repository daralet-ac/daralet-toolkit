namespace DaraletPlayer.Feed {
    /// <summary>
    /// The game's own ping: the client's CM_Character::Event_RequestPing sends game action 0x01E9 (PingRequest), which
    /// daralet-ACE answers with game event 0x01EA (PingResponse, GameEventPingResponse: nothing in it but the event's
    /// header). The server handles game actions on its world thread's tick (InboundMessageManager queues them), so the time
    /// from one to the other is the lag a player feels: the network both ways, plus however long the server takes to get to
    /// it.
    /// </summary>
    public static class PingMessages {
        public const uint PingResponse = 0x01EA;

        /// <summary>Whether a message from the server is a PingResponse (0xF7B0, character, sequence, event type).</summary>
        public static bool IsPingResponse(byte[] message) {
            if (message == null || message.Length < 16) {
                return false;
            }

            return ReadUInt32(message, 0) == CooldownMessages.GameEvent && ReadUInt32(message, 12) == PingResponse;
        }

        private static uint ReadUInt32(byte[] data, int at) {
            return (uint)(data[at] | data[at + 1] << 8 | data[at + 2] << 16 | data[at + 3] << 24);
        }
    }
}
