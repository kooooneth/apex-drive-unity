using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// A room code is the host's network address in eight letters and digits (e.g. K7QM-4XWD): the IPv4 address, which of the eight ports the
    /// host opened and a 5-bit room key that the host checks, so a mistyped code cannot join the wrong game.
    /// </summary>
    public static class RoomCode
    {
        const string ALPHA = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";           // no 0/O or 1/I to mix up

        public static string Encode(IPAddress ip, int portOffset, int key)
        {
            byte[] b = ip.GetAddressBytes();
            ulong v = ((ulong)b[0] << 32) | ((ulong)b[1] << 24) | ((ulong)b[2] << 16) | ((ulong)b[3] << 8) | (ulong)(uint)(((portOffset & 7) << 5) | (key & 31));
            var sb = new StringBuilder();
            for (int i = 7; i >= 0; i--) sb.Append(ALPHA[(int)((v >> (i * 5)) & 31)]);
            return sb.ToString().Insert(4, "-");
        }

        public static bool TryDecode(string text, out IPAddress ip, out int portOffset, out int key)
        {
            ip = null; portOffset = 0; key = 0;
            if (text == null) return false;
            ulong v = 0; int n = 0;
            foreach (char raw in text.ToUpperInvariant())
            {
                if (raw == '-' || raw == ' ') continue;
                int d = ALPHA.IndexOf(raw);
                if (d < 0) return false;
                v = (v << 5) | (uint)d; n++;
            }
            if (n != 8) return false;
            ip = new IPAddress(new byte[] { (byte)(v >> 32), (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8) });
            portOffset = (int)((v >> 5) & 7); key = (int)(v & 31);
            return true;
        }

        /// <summary>The address other computers on your network can reach you at.</summary>
        public static IPAddress LocalIp()
        {
            try
            {
                using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    s.Connect("8.8.8.8", 65530);                      // no packet is sent for a UDP connect: it only picks the network card
                    var ep = s.LocalEndPoint as IPEndPoint;
                    if (ep != null && !IPAddress.IsLoopback(ep.Address)) return ep.Address;
                }
            }
            catch (Exception) { }
            try
            {
                foreach (var a in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)) return a;
            }
            catch (Exception) { }
            return IPAddress.Loopback;
        }
    }

    public class NetPlayer
    {
        public int slot; public string name = "Player"; public int body, accent, pattern, number;
        public IPEndPoint ep; public float lastSeen;
    }

    /// <summary>
    /// Another person's car. Their game sends where the car is 20 times a second; here it is smoothed and carried forward with the car's speed,
    /// so it moves evenly even though the packets arrive in steps.
    /// </summary>
    public class NetRacer : Racer
    {
        public int slot;
        WheelRig wheels; DrsFlap flap;
        Vector3 pos0, vel; Quaternion rot0 = Quaternion.identity; float recvAt = -1f, dist0;
        public float lastPacket;

        public NetRacer(GameObject visual, int slotId, string name, float now)
        {
            go = visual; slot = slotId; label = name; wheels = visual.GetComponent<WheelRig>(); lastPacket = now;
        }

        public void Receive(string[] f, float now)
        {
            // S|slot|x|y|z|rx|ry|rz|speed|dist|lap|finish|pit|box|pitStart|pitEnd|compound|wear|dnf
            if (f.Length < 19) return;
            var ci = CultureInfo.InvariantCulture;
            try
            {
                pos0 = new Vector3(float.Parse(f[2], ci), float.Parse(f[3], ci), float.Parse(f[4], ci));
                rot0 = Quaternion.Euler(float.Parse(f[5], ci), float.Parse(f[6], ci), float.Parse(f[7], ci));
                speed = float.Parse(f[8], ci); dist0 = float.Parse(f[9], ci); lap = int.Parse(f[10], ci);
                finishTime = float.Parse(f[11], ci);
                bool wasPit = pitStopActive;
                pitStopActive = f[12] == "1"; pitBox = int.Parse(f[13], ci);
                pitStopStart = float.Parse(f[14], ci); pitStopEnd = float.Parse(f[15], ci);
                compound = (Compound)Mathf.Clamp(int.Parse(f[16], ci), 0, 2); tireWear = float.Parse(f[17], ci);
                dnf = f[18] == "1";
                if (f.Length > 19) drsOpen = float.Parse(f[19], ci);
                if (wasPit != pitStopActive && wheels != null) wheels.SetWheelsVisible(true);
            }
            catch (Exception) { return; }
            bool first = recvAt < 0f;
            recvAt = now; lastPacket = now;
            dist = dist0;
            if (first && go != null) { go.transform.position = pos0; go.transform.rotation = rot0; }
        }

        public void Tick(float dt, float now)
        {
            if (recvAt < 0f || go == null || !go.activeSelf) return;
            float age = Mathf.Min(0.25f, now - recvAt);
            Vector3 fwd = rot0 * Vector3.forward;
            Vector3 target = pos0 + fwd * (speed * age);
            float k = 1f - Mathf.Exp(-14f * dt);
            go.transform.position = Vector3.Lerp(go.transform.position, target, k);
            go.transform.rotation = Quaternion.Slerp(go.transform.rotation, rot0, k);
            dist = dist0 + speed * age;
            if (wheels != null) wheels.Animate(speed, 0f, dt);
            if (flap == null) flap = go.GetComponent<DrsFlap>();
            if (flap != null) flap.Set(drsOpen);
        }
    }

    /// <summary>
    /// Online play over UDP with no server: one player hosts (the room code is their address), the others type the code. The host keeps the
    /// lobby (circuit, laps, who is in), says when the race starts, and passes everybody's car positions on to the others.
    /// </summary>
    public class NetSession
    {
        public enum Role { None, Host, Client }
        public const int BASE_PORT = 47777, MAX_PLAYERS = 6;

        public Role role = Role.None;
        public string code = "", status = "", error = "";
        public bool inRace;                                     // set by the race manager while an online race is on (the host refuses new players then)
        public int mySlot;
        public readonly List<NetPlayer> roster = new List<NetPlayer>();           // everybody in the room, you included, in slot order
        public readonly Dictionary<int, NetRacer> cars = new Dictionary<int, NetRacer>();
        // the lobby settings (the host chooses, everybody else sees)
        public int trackIndex, laps = 3, raceTime, startType;

        // set when the host says go
        public bool startPending; public int startSeed;
        public List<NetPlayer> startRoster;

        UdpClient udp; IPEndPoint hostEp; int key, portOffset;
        float nextSend, nextLobby, nextJoin, joinSince, lastHostPacket, nextStartResend;
        int lastStartSeed = -1; float resendUntil; string startMessage = "";
        bool joined;

        public bool Active { get { return role != Role.None; } }
        public bool Joined { get { return role == Role.Host || joined; } }

        static string Clean(string s, int max)
        {
            if (s == null) return "Player";
            var sb = new StringBuilder();
            foreach (char c in s) { if (c == '|' || c == ';' || c == ',' || c == '\n') continue; sb.Append(c); if (sb.Length >= max) break; }
            return sb.Length == 0 ? "Player" : sb.ToString();
        }

        NetPlayer Me()
        {
            return new NetPlayer
            {
                slot = mySlot, name = Clean(RacerProfile.DisplayName, 14), body = GameSave.bodyColor, accent = GameSave.accentColor, pattern = GameSave.pattern, number = RacerProfile.number
            };
        }

        // ---------------------------------------------------------------- opening and closing
        public bool Host(string addressOverride)
        {
            Close();
            error = "";
            try
            {
                int off = 0;
                for (; off < 8; off++)
                {
                    try { udp = new UdpClient(BASE_PORT + off); break; }
                    catch (SocketException) { udp = null; }
                }
                if (udp == null) { error = "Could not open a network port (is another copy of the game hosting, or is the firewall blocking it?)."; return false; }
                Quiet(udp);
                portOffset = off; key = UnityEngine.Random.Range(0, 32);
                IPAddress ip;
                if (string.IsNullOrEmpty(addressOverride) || !IPAddress.TryParse(addressOverride.Trim(), out ip) || ip.AddressFamily != AddressFamily.InterNetwork) ip = RoomCode.LocalIp();
                code = RoomCode.Encode(ip, portOffset, key);
                role = Role.Host; mySlot = 0; joined = true;
                roster.Clear(); roster.Add(Me());
                status = "Room open. Tell your friends the code.";
                return true;
            }
            catch (Exception e) { error = "Could not host: " + e.Message; Close(); return false; }
        }

        public bool Join(string roomCode)
        {
            Close();
            error = "";
            IPAddress ip;
            if (!RoomCode.TryDecode(roomCode, out ip, out portOffset, out key)) { error = "That is not a room code. It has 8 letters and digits, like K7QM-4XWD."; return false; }
            try
            {
                udp = new UdpClient(0);
                Quiet(udp);
                hostEp = new IPEndPoint(ip, BASE_PORT + portOffset);
                code = roomCode.ToUpperInvariant().Trim();
                role = Role.Client; joined = false; joinSince = Time.unscaledTime; nextJoin = 0f; lastHostPacket = Time.unscaledTime;
                roster.Clear();
                status = "Looking for the room...";
                return true;
            }
            catch (Exception e) { error = "Could not join: " + e.Message; Close(); return false; }
        }

        static void Quiet(UdpClient u)
        {
            // Windows reports "connection reset" on a UDP socket when a peer has gone away; that must not break the receive loop
            try { u.Client.IOControl((IOControlCode)(-1744830452), new byte[] { 0 }, null); } catch (Exception) { }
        }

        public void Close()
        {
            if (udp != null)
            {
                try { if (role != Role.None && Joined) SendRaw("BYE|" + mySlot); } catch (Exception) { }
                try { udp.Close(); } catch (Exception) { }
                udp = null;
            }
            role = Role.None; joined = false; roster.Clear(); cars.Clear(); startPending = false; inRace = false; status = ""; code = "";
        }

        // ---------------------------------------------------------------- sending
        void SendTo(string msg, IPEndPoint ep)
        {
            if (udp == null || ep == null) return;
            try { byte[] b = Encoding.UTF8.GetBytes(msg); udp.Send(b, b.Length, ep); } catch (Exception) { }
        }

        void SendRaw(string msg)
        {
            if (role == Role.Client) SendTo(msg, hostEp);
            else if (role == Role.Host) foreach (var p in roster) if (p.slot != 0) SendTo(msg, p.ep);
        }

        string RosterText(List<NetPlayer> list)
        {
            var sb = new StringBuilder();
            foreach (var p in list) sb.Append(p.slot).Append(',').Append(Clean(p.name, 14)).Append(',').Append(p.body).Append(',').Append(p.accent).Append(',').Append(p.pattern).Append(',').Append(p.number).Append(';');
            return sb.ToString();
        }

        static List<NetPlayer> ParseRoster(string text)
        {
            var l = new List<NetPlayer>();
            foreach (var part in text.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var f = part.Split(',');
                if (f.Length < 6) continue;
                int slot, body, accent, pattern, number;
                if (!int.TryParse(f[0], out slot) || !int.TryParse(f[2], out body) || !int.TryParse(f[3], out accent) || !int.TryParse(f[4], out pattern) || !int.TryParse(f[5], out number)) continue;
                l.Add(new NetPlayer { slot = slot, name = f[1], body = body, accent = accent, pattern = pattern, number = number });
            }
            return l;
        }

        string LobbyMessage() { return "LOBBY|" + trackIndex + "|" + laps + "|" + raceTime + "|" + startType + "|" + (inRace ? 1 : 0) + "|" + RosterText(roster); }

        /// <summary>The host presses start: everybody gets the grid and the random seed (sent several times, UDP can lose a packet).</summary>
        public void HostStart()
        {
            if (role != Role.Host) return;
            startSeed = UnityEngine.Random.Range(1, 1000000);
            startRoster = new List<NetPlayer>(roster);
            startMessage = "START|" + startSeed + "|" + trackIndex + "|" + laps + "|" + raceTime + "|" + startType + "|" + RosterText(roster);
            resendUntil = Time.unscaledTime + 1.5f; nextStartResend = Time.unscaledTime + 0.15f;
            SendRaw(startMessage);
            lastStartSeed = startSeed;
            startPending = true;
        }

        public void SendState(PlayerCar p, float now)
        {
            if (!Joined || p == null || p.go == null || Time.unscaledTime < nextSend) return;
            nextSend = Time.unscaledTime + 0.05f;
            var ci = CultureInfo.InvariantCulture;
            Vector3 w = p.go.transform.position, e = p.go.transform.rotation.eulerAngles;
            var sb = new StringBuilder("S|");
            sb.Append(mySlot).Append('|')
              .Append(w.x.ToString("0.###", ci)).Append('|').Append(w.y.ToString("0.###", ci)).Append('|').Append(w.z.ToString("0.###", ci)).Append('|')
              .Append(e.x.ToString("0.#", ci)).Append('|').Append(e.y.ToString("0.#", ci)).Append('|').Append(e.z.ToString("0.#", ci)).Append('|')
              .Append(p.speed.ToString("0.##", ci)).Append('|').Append(p.dist.ToString("0.##", ci)).Append('|').Append(p.lap).Append('|')
              .Append(p.finishTime.ToString("0.###", ci)).Append('|').Append(p.pitStopActive ? 1 : 0).Append('|').Append(p.pitBox).Append('|')
              .Append(p.pitStopStart.ToString("0.##", ci)).Append('|').Append(p.pitStopEnd.ToString("0.##", ci)).Append('|')
              .Append((int)p.compound).Append('|').Append(p.tireWear.ToString("0.#", ci)).Append('|').Append(p.dnf ? 1 : 0).Append('|').Append(p.drsOpen.ToString("0.##", ci));
            string msg = sb.ToString();
            if (role == Role.Client) SendTo(msg, hostEp);
            else foreach (var pl in roster) if (pl.slot != 0) SendTo(msg, pl.ep);
        }

        // ---------------------------------------------------------------- receiving and housekeeping
        public void Poll(float now)
        {
            if (udp == null) return;
            float t = Time.unscaledTime;
            int guard = 0;
            while (udp != null && guard++ < 400)
            {
                int avail;
                try { avail = udp.Available; } catch (Exception) { break; }
                if (avail <= 0) break;
                IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                byte[] data;
                try { data = udp.Receive(ref from); } catch (SocketException) { continue; } catch (Exception) { break; }
                string text = Encoding.UTF8.GetString(data);
                string[] f = text.Split('|');
                if (f.Length == 0) continue;
                try { Handle(f, text, from, now, t); } catch (Exception) { }
            }
            if (udp == null) return;

            if (role == Role.Client)
            {
                if (!joined)
                {
                    if (t >= nextJoin)
                    {
                        nextJoin = t + 0.5f;
                        var me = Me();
                        SendTo("JOIN|" + (key & 31) + "|" + Clean(me.name, 14) + "|" + me.body + "|" + me.accent + "|" + me.pattern + "|" + me.number, hostEp);
                    }
                    if (t - joinSince > 8f) { error = "No answer from that room. Check the code, that you are on the same network (or the host has opened the port), and the firewall."; Close(); }
                }
                else
                {
                    if (t >= nextLobby) { nextLobby = t + 1f; SendTo("PING|" + mySlot, hostEp); }
                    if (t - lastHostPacket > 8f) { error = "Lost the connection to the host."; Close(); }
                }
            }
            else if (role == Role.Host)
            {
                if (t >= nextLobby) { nextLobby = t + 1f; SendRaw(LobbyMessage()); }
                if (resendUntil > t && startMessage.Length > 0 && t >= nextStartResend) { nextStartResend = t + 0.15f; SendRaw(startMessage); }
                for (int i = roster.Count - 1; i >= 1; i--)
                    if (t - roster[i].lastSeen > 8f) { cars.Remove(roster[i].slot); roster.RemoveAt(i); nextLobby = 0f; }
            }
        }

        int FreeSlot()
        {
            for (int s = 1; s < MAX_PLAYERS; s++)
            {
                bool used = false;
                foreach (var p in roster) if (p.slot == s) used = true;
                if (!used) return s;
            }
            return -1;
        }

        void Handle(string[] f, string text, IPEndPoint from, float now, float t)
        {
            string kind = f[0];
            var ci = CultureInfo.InvariantCulture;
            if (role == Role.Host)
            {
                if (kind == "JOIN" && f.Length >= 7)
                {
                    int k;
                    if (!int.TryParse(f[1], out k) || k != key) { SendTo("DENY|That code is not for this room.", from); return; }
                    NetPlayer existing = null;
                    foreach (var p in roster) if (p.slot != 0 && p.ep != null && p.ep.Equals(from)) existing = p;
                    if (existing == null)
                    {
                        if (inRace) { SendTo("DENY|A race is in progress, try again when it is over.", from); return; }
                        int slot = FreeSlot();
                        if (slot < 0) { SendTo("DENY|The room is full.", from); return; }
                        existing = new NetPlayer { slot = slot, ep = from };
                        roster.Add(existing);
                        roster.Sort((a, b) => a.slot.CompareTo(b.slot));
                    }
                    existing.lastSeen = t;
                    existing.name = Clean(f[2], 14);
                    int.TryParse(f[3], out existing.body); int.TryParse(f[4], out existing.accent); int.TryParse(f[5], out existing.pattern); int.TryParse(f[6], out existing.number);
                    SendTo("ACCEPT|" + existing.slot, from);
                    nextLobby = 0f;
                    return;
                }
                // everything else must come from somebody in the room
                NetPlayer who = null;
                foreach (var p in roster) if (p.slot != 0 && p.ep != null && p.ep.Equals(from)) who = p;
                if (who == null) return;
                who.lastSeen = t;
                if (kind == "BYE") { cars.Remove(who.slot); roster.Remove(who); nextLobby = 0f; }
                else if (kind == "S" && f.Length >= 19)
                {
                    int slot; if (!int.TryParse(f[1], out slot) || slot != who.slot) return;
                    foreach (var p in roster) if (p.slot != 0 && p.slot != slot) SendTo(text, p.ep);         // pass it on to the others
                    NetRacer nr; if (cars.TryGetValue(slot, out nr)) nr.Receive(f, now);
                }
                return;
            }

            // ---- client ----
            if (role != Role.Client) return;
            if (!from.Equals(hostEp)) return;
            lastHostPacket = t;
            if (kind == "ACCEPT" && f.Length >= 2) { int s; if (int.TryParse(f[1], out s)) { mySlot = s; joined = true; status = "Joined the room."; nextLobby = 0f; } }
            else if (kind == "DENY") { error = f.Length > 1 ? f[1] : "The host refused."; Close(); }
            else if (kind == "BYE") { error = "The host closed the room."; Close(); }
            else if (kind == "LOBBY" && f.Length >= 7)
            {
                int.TryParse(f[1], out trackIndex); int.TryParse(f[2], out laps); int.TryParse(f[3], out raceTime); int.TryParse(f[4], out startType);
                roster.Clear(); roster.AddRange(ParseRoster(f[6]));
            }
            else if (kind == "START" && f.Length >= 7)
            {
                int seed; if (!int.TryParse(f[1], out seed) || seed == lastStartSeed) return;
                lastStartSeed = seed; startSeed = seed;
                int.TryParse(f[2], out trackIndex); int.TryParse(f[3], out laps); int.TryParse(f[4], out raceTime); int.TryParse(f[5], out startType);
                startRoster = ParseRoster(f[6]);
                startPending = true;
            }
            else if (kind == "S" && f.Length >= 19)
            {
                int slot; if (!int.TryParse(f[1], out slot)) return;
                NetRacer nr; if (cars.TryGetValue(slot, out nr)) nr.Receive(f, now);
            }
        }

        /// <summary>Remote cars whose packets have stopped for a few seconds (closed game, lost connection) retire from the race.</summary>
        public void CheckTimeouts(float now, Action<NetRacer> onLost)
        {
            foreach (var kv in cars)
            {
                var c = kv.Value;
                if (!c.gone && now - c.lastPacket > 5f) onLost(c);
            }
        }

        /// <summary>A short line for the corner of the race screen.</summary>
        public string Badge()
        {
            return "ONLINE  " + code + "   " + roster.Count + (roster.Count == 1 ? " player" : " players");
        }
    }
}
