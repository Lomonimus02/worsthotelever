using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace WorstHotel
{
    public static class HotelSaveStore
    {
        private const int Version = 1;
        private const long MaxFileBytes = 8 * 1024 * 1024;
        private static readonly object Gate = new object();

        [Serializable]
        private sealed class Envelope
        {
            public string format;
            public int version;
            public string checksum;
            public string payload;
        }

        // Explicit new-game action only; ordinary Save deliberately cannot replace another world.
        public static void StartNew(HotelState state, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Не задан путь сохранения.", nameof(path));
            lock (Gate)
            {
                Validate(state);
                string fullPath = Path.GetFullPath(path);
                string directory = Path.GetDirectoryName(fullPath);
                Directory.CreateDirectory(directory);
                string backup = fullPath + ".bak";
                // A directory in place of a slot is an error, not an absent previous world.
                if (Directory.Exists(fullPath) || Directory.Exists(backup)) throw new IOException("Путь слота занят каталогом.");
                bool hadPrimary = File.Exists(fullPath), hadBackup = File.Exists(backup);
                string token = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'") + "-" + Guid.NewGuid().ToString("N");
                string temporary = fullPath + ".new-" + token + ".tmp";
                string nextBackup = fullPath + ".backup-" + token + ".tmp";
                string archivedPrimary = null, archivedBackup = null;
                bool replacementStarted = false;
                try
                {
                    // Save to a distinct path first; invalid new data never touches an old slot.
                    Save(state, temporary);
                    CopyDurable(temporary, nextBackup);
                    Read(nextBackup);
                    if (hadPrimary || hadBackup)
                    {
                        string archive = Path.Combine(directory, "archive", Path.GetFileName(fullPath) + "-" + token);
                        Directory.CreateDirectory(archive);
                        if (hadPrimary)
                        {
                            archivedPrimary = Path.Combine(archive, "primary.json");
                            CopyDurable(fullPath, archivedPrimary);
                        }
                        if (hadBackup)
                        {
                            archivedBackup = Path.Combine(archive, "backup.json");
                            CopyDurable(backup, archivedBackup);
                        }
                    }
                    replacementStarted = true;
                    Install(temporary, fullPath);
                    Install(nextBackup, backup);
                }
                catch (Exception failure)
                {
                    if (replacementStarted)
                    {
                        // Archive copies are immutable. Restore through same-volume atomic replacements,
                        // including the original backup, if the second installation failed.
                        var errors = new List<Exception> { failure };
                        try { Restore(archivedPrimary, fullPath); } catch (Exception error) { errors.Add(error); }
                        try { Restore(archivedBackup, backup); } catch (Exception error) { errors.Add(error); }
                        if (errors.Count > 1)
                            throw new AggregateException("Не удалось полностью восстановить слот. Старые файлы сохранены в archive.", errors);
                    }
                    throw;
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                    if (File.Exists(nextBackup)) File.Delete(nextBackup);
                }
            }
        }

        private static void Install(string source, string destination)
        {
            if (File.Exists(destination)) File.Replace(source, destination, null);
            else File.Move(source, destination);
        }

        private static void Restore(string archive, string destination)
        {
            if (archive == null)
            {
                // No previous file existed here: only an uncommitted new-game snapshot is removed.
                if (File.Exists(destination)) File.Delete(destination);
                return;
            }
            // An atomic install can fail while its destination remains untouched (e.g. a read lock).
            // Do not try to replace that already-correct file during rollback.
            if (File.Exists(destination) && IdenticalFiles(archive, destination)) return;
            string temporary = destination + ".rollback-" + Guid.NewGuid().ToString("N") + ".tmp";
            try { CopyDurable(archive, temporary); Install(temporary, destination); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static void CopyDurable(string source, string destination)
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                input.CopyTo(output);
                output.Flush(true);
            }
            if (!IdenticalFiles(source, destination)) throw new IOException("Проверка архивной копии не пройдена; исходный слот оставлен на месте.");
        }

        private static bool IdenticalFiles(string source, string destination)
        {
            using (SHA256 hash = SHA256.Create())
            using (var original = File.OpenRead(source))
            using (var copied = File.OpenRead(destination))
            {
                string expected = BitConverter.ToString(hash.ComputeHash(original));
                string actual = BitConverter.ToString(hash.ComputeHash(copied));
                return expected == actual;
            }
        }

        public static void Save(HotelState state, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Не задан путь сохранения.", nameof(path));
            lock (Gate)
            {
                Validate(state);
                // Serializing now freezes a consistent snapshot; caller must invoke on the host thread.
                string payload = JsonUtility.ToJson(state);
                Validate(JsonUtility.FromJson<HotelState>(payload));
                var envelope = new Envelope { format = "WorstHotelSave", version = Version, payload = payload, checksum = Hash(payload) };
                string json = JsonUtility.ToJson(envelope);
                string fullPath = Path.GetFullPath(path);
                string directory = Path.GetDirectoryName(fullPath);
                Directory.CreateDirectory(directory);
                string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                string backup = fullPath + ".bak";
                bool primaryValid = false;
                if (File.Exists(fullPath))
                {
                    HotelState previous = ReadRecoverable(fullPath);
                    primaryValid = previous != null;
                    if (previous != null && previous.worldId != state.worldId)
                        throw new InvalidOperationException("Слот принадлежит другому отелю. Используйте отдельный файл.");
                }
                if (!primaryValid && File.Exists(backup))
                {
                    HotelState previous = ReadRecoverable(backup);
                    if (previous != null && previous.worldId != state.worldId)
                        throw new InvalidOperationException("Резервная копия принадлежит другому отелю. Используйте отдельный файл.");
                }
                try
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(json);
                    using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                    {
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }
                    Read(temporary); // Validate the actual bytes before touching a good checkpoint.
                    if (File.Exists(fullPath))
                    {
                        // File.Replace is atomic on the target Windows filesystem. Do not fall back to
                        // delete+move: an unsupported volume must fail while leaving the old save intact.
                        File.Replace(temporary, fullPath, primaryValid ? backup : null);
                    }
                    else File.Move(temporary, fullPath);
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }
        }

        public static HotelState Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Не задан путь сохранения.", nameof(path));
            lock (Gate)
            {
                string fullPath = Path.GetFullPath(path);
                string backup = fullPath + ".bak";
                if (!File.Exists(fullPath) && !File.Exists(backup)) return null;
                HotelState state = File.Exists(fullPath) ? ReadRecoverable(fullPath) : null;
                bool recovered = state == null;
                if (state == null && File.Exists(backup)) state = ReadRecoverable(backup);
                if (state == null) throw new InvalidDataException("Сохранение и резервная копия повреждены. Файлы оставлены без изменений.");
                ReleasePlayers(state);
                if (recovered) state.notice = "Загружена резервная копия отеля: основное сохранение повреждено или отсутствует.";
                Validate(state);
                return state;
            }
        }

        private static HotelState ReadRecoverable(string path)
        {
            try { return Read(path); }
            // Future versions must not silently fall back to an older checkpoint and be overwritten.
            catch (InvalidDataException) { return null; }
            catch (FileNotFoundException) { return null; }
            // A temporarily locked/inaccessible file is not corrupt. Loading an older backup
            // here would silently roll back a healthy hotel and overwrite it on the next save.
        }

        private static HotelState Read(string path)
        {
            var info = new FileInfo(path);
            if (info.Length < 2 || info.Length > MaxFileBytes) throw new InvalidDataException("Недопустимый размер сохранения.");
            Envelope envelope;
            try { envelope = JsonUtility.FromJson<Envelope>(File.ReadAllText(path, Encoding.UTF8)); }
            catch (ArgumentException error) { throw new InvalidDataException("Неверный JSON сохранения.", error); }
            if (envelope == null || envelope.format != "WorstHotelSave") throw new InvalidDataException("Неизвестный формат сохранения.");
            if (envelope.version > Version) throw new NotSupportedException("Сохранение создано более новой версией игры.");
            if (envelope.version != Version || string.IsNullOrEmpty(envelope.payload) || string.IsNullOrEmpty(envelope.checksum))
                throw new InvalidDataException("Неподдерживаемая или неполная схема сохранения.");
            if (!string.Equals(envelope.checksum, Hash(envelope.payload), StringComparison.Ordinal)) throw new InvalidDataException("Контрольная сумма сохранения не совпадает.");
            HotelState state;
            try { state = JsonUtility.FromJson<HotelState>(envelope.payload); }
            catch (ArgumentException error) { throw new InvalidDataException("Неверные данные отеля.", error); }
            Validate(state);
            return state;
        }

        private static string Hash(string text)
        {
            using (SHA256 hash = SHA256.Create())
            {
                byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(text));
                return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            }
        }

        private static void ReleasePlayers(HotelState state)
        {
            foreach (ItemState item in state.items)
            {
                if (item.holder >= 0)
                {
                    PlayerState player = state.players.Find(p => p.id == (ulong)item.holder);
                    item.position = HotelSimulation.SafePosition(player == null ? item.position : player.position);
                    item.holder = -1;
                    item.placedRoom = 0;
                }
                else if (!HotelSimulation.Walkable(item.position)) item.position = HotelLayout.Spawn;
            }
            state.players.Clear();
            ItemState cart = state.items.Find(i => i.kind == "cart" && !i.consumed);
            int slot = 0;
            foreach (ItemState item in state.items)
                if (!item.consumed && item.placedRoom == -1 && cart != null)
                    item.position = cart.position + new Vector3(slot++ == 0 ? -.23f : .23f, .25f, 0);
        }

        // Public for batch/integration diagnostics. Invalid data is rejected, never replaced by a new hotel.
        public static void Validate(HotelState state)
        {
            Require(state != null, "Отель отсутствует.");
            if (state.version > Version) throw new NotSupportedException("Состояние отеля создано более новой версией игры.");
            Require(state.version == Version, "Неподдерживаемая версия состояния отеля.");
            Require(!string.IsNullOrWhiteSpace(state.worldId) && state.worldId.Length <= 128, "Отсутствует ID отеля.");
            Require(state.day > 0 && state.day < 1000000 && state.nextGuest > 0 && state.nextGuest < int.MaxValue - 10, "Неверные счётчики отеля.");
            Require(state.phase == "preparation" || state.phase == "open" || state.phase == "closing" || state.phase == "summary", "Неизвестная фаза смены.");
            Require(HotelSimulation.Finite(state.dayLength) && state.dayLength > 0 && state.dayLength <= 86400, "Неверная длина дня.");
            Require(HotelSimulation.Finite(state.time) && state.time >= 0 && state.time <= state.dayLength + .01f, "Неверное время смены.");
            Require(state.arrivals >= 0 && state.arrivals <= 4 && state.served >= 0 && state.served <= 4, "Неверное число гостей.");
            Require(Math.Abs((long)state.cash) < 100000000 && state.earned >= 0 && state.earned < 100000000 && state.expenses >= 0 && state.expenses < 100000000, "Неверные денежные значения.");
            Require(state.linenStock >= 0 && state.linenStock <= 10000 && state.towelStock >= 0 && state.towelStock <= 10000, "Неверные запасы.");
            Require(state.rooms != null && state.rooms.Count == 4, "Должно быть четыре номера.");
            Require(state.guests != null && state.guests.Count <= 4 && state.items != null && state.items.Count <= 10000 && state.players != null && state.players.Count <= 2, "Неверные списки сущностей.");
            ValidateStrings(state.reviews, 120);
            ValidateStrings(state.ledger, 256);
            var rooms = new Dictionary<int, RoomState>();
            foreach (RoomState room in state.rooms)
            {
                Require(room != null && room.number >= 101 && room.number <= 104 && !rooms.ContainsKey(room.number), "Повторный или неизвестный номер.");
                Require(room.bed >= 0 && room.bed <= 2 && HotelSimulation.Finite(room.water) && room.water >= 0 && room.water <= 1, "Повреждено состояние номера.");
                Require(room.guestId >= 0 && room.upgraded == state.betterBeds, "Неверные связи или улучшения номера.");
                rooms.Add(room.number, room);
            }
            var guests = new Dictionary<int, GuestState>();
            foreach (GuestState guest in state.guests)
            {
                Require(guest != null && guest.id > 0 && guest.id < state.nextGuest && !guests.ContainsKey(guest.id), "Повторный или неверный ID гостя.");
                Require(guest.stage == "queue" || guest.stage == "walking" || guest.stage == "staying" || guest.stage == "checkout" || guest.stage == "leaving" || guest.stage == "gone", "Неизвестное состояние гостя.");
                Require(HotelSimulation.Finite(guest.position) && HotelSimulation.Walkable(guest.position), "Гость вне доступного отеля.");
                Require(HotelSimulation.Finite(guest.satisfaction) && guest.satisfaction >= 0 && guest.satisfaction <= 100, "Неверная удовлетворённость.");
                Require(HotelSimulation.Finite(guest.waited) && guest.waited >= 0 && HotelSimulation.Finite(guest.stay) && guest.stay >= 0, "Неверные таймеры гостя.");
                ValidateStrings(guest.memories, 64);
                Require(!string.IsNullOrEmpty(guest.name) && guest.name.Length <= 128, "Отсутствует имя гостя.");
                bool occupying = guest.stage == "walking" || guest.stage == "staying" || guest.stage == "checkout";
                Require(!occupying || (rooms.ContainsKey(guest.room) && rooms[guest.room].guestId == guest.id && !guest.paid), "Нарушена связь гостя с номером.");
                Require(guest.room == 0 || rooms.ContainsKey(guest.room), "Гость ссылается на неизвестный номер.");
                Require(!guest.paid || guest.stage == "leaving" || guest.stage == "gone", "Оплаченный гость всё ещё живёт в номере.");
                guests.Add(guest.id, guest);
            }
            foreach (RoomState room in rooms.Values)
                if (room.guestId != 0)
                    Require(guests.TryGetValue(room.guestId, out GuestState g) && g.room == room.number && (g.stage == "walking" || g.stage == "staying" || g.stage == "checkout"), "Номер ссылается на отсутствующего жильца.");
            var players = new Dictionary<ulong, PlayerState>();
            foreach (PlayerState player in state.players)
            {
                Require(player != null && player.id <= long.MaxValue && !players.ContainsKey(player.id), "Повторный или неверный ID сотрудника.");
                Require(HotelSimulation.Walkable(player.position) && HotelSimulation.Finite(player.yaw) && HotelSimulation.Finite(player.pitch), "Повреждена позиция сотрудника.");
                Require(HotelSimulation.Finite(player.workProgress) && player.workProgress >= 0 && player.workProgress <= 1 && HotelSimulation.Finite(player.workLastSeen) && player.workLastSeen >= 0, "Повреждён прогресс работы.");
                players.Add(player.id, player);
            }
            var items = new Dictionary<string, ItemState>();
            int toolboxes = 0, mops = 0, carts = 0, cargo = 0;
            foreach (ItemState item in state.items)
            {
                Require(item != null && !string.IsNullOrEmpty(item.id) && item.id.Length <= 128 && !items.ContainsKey(item.id), "Повторный или неверный ID предмета.");
                Require(item.kind == "toolbox" || item.kind == "mop" || item.kind == "cart" || item.kind == "bag" || item.kind == "linen" || item.kind == "towel" || item.kind == "dirtylinen" || item.kind == "trashbag", "Неизвестный тип предмета.");
                Require(HotelSimulation.Finite(item.position) && item.holder >= -1, "Повреждена позиция или владелец предмета.");
                Require(item.placedRoom == -1 || item.placedRoom == 0 || rooms.ContainsKey(item.placedRoom), "Неверное размещение предмета.");
                Require(item.kind == "bag" ? guests.ContainsKey(item.ownerGuest) : item.ownerGuest == 0, "Повреждена собственность багажа.");
                if (item.holder >= 0)
                    Require(!item.consumed && item.placedRoom == 0 && players.TryGetValue((ulong)item.holder, out PlayerState p) && p.held == item.id, "Предмет удерживает отсутствующий сотрудник.");
                if (!item.consumed)
                {
                    if (item.kind == "toolbox") toolboxes++;
                    if (item.kind == "mop") mops++;
                    if (item.kind == "cart") carts++;
                    if (item.placedRoom == -1)
                    {
                        Require(item.kind == "bag" && item.holder == -1 && state.cartUpgrade, "Неверный груз тележки.");
                        cargo++;
                    }
                    if (item.placedRoom > 0)
                        Require(item.kind == "bag" && item.holder == -1 && guests[item.ownerGuest].room == item.placedRoom, "Багаж размещён в чужом номере.");
                }
                items.Add(item.id, item);
            }
            Require(toolboxes == (state.secondToolbox ? 2 : 1) && mops == 1 && carts == (state.cartUpgrade ? 1 : 0) && cargo <= 2, "Потерян или продублирован постоянный инструмент.");
            foreach (PlayerState player in players.Values)
                if (!string.IsNullOrEmpty(player.held))
                    Require(items.TryGetValue(player.held, out ItemState i) && !i.consumed && i.holder == (long)player.id, "Нарушена ссылка руки на предмет.");
            foreach (GuestState guest in guests.Values)
            {
                if (guest.stage == "gone" || guest.stage == "leaving") continue;
                int count = 0;
                foreach (ItemState item in items.Values)
                    if (item.kind == "bag" && item.ownerGuest == guest.id && !item.consumed) count++;
                Require(count == 1, "Потерян или продублирован чемодан гостя.");
            }
        }

        private static void ValidateStrings(List<string> strings, int max)
        {
            Require(strings != null && strings.Count <= max, "Повреждён журнал отеля.");
            foreach (string text in strings) Require(text != null && text.Length <= 8192, "Повреждена запись журнала.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }
    }
}
