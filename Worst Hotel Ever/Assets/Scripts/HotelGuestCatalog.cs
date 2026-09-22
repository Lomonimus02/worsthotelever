using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WorstHotel
{
    [Serializable] public sealed class HotelRequestDefinition
    {
        public string id, kind;
        public float delaySeconds, graceSeconds;
    }
    [Serializable] public sealed class HotelGuestProfile
    {
        public string id, trait, requestId;
        public float patienceWarning, patienceLimit, staySeconds, luggageGrace, checkoutWarning, checkoutLimit;
    }

    // Instances own their parsed data. No mutable static catalogue or mutable borrowed definitions.
    public sealed class HotelGuestCatalog
    {
        [Serializable] private sealed class Document
        {
            public int version = 0; // Assigned by JsonUtility; absence is an invalid catalogue.
            public HotelGuestProfile[] profiles;
            public HotelRequestDefinition[] requests;
        }
        private readonly Document data;
        public int Version { get { return data.version; } }
        public string Warning { get; private set; }
        private HotelGuestCatalog(Document document, string warning = "") { data = document; Warning = warning; }

        public static HotelGuestCatalog Legacy(string warning = "")
        {
            return new HotelGuestCatalog(new Document { profiles = new HotelGuestProfile[0], requests = new HotelRequestDefinition[0] }, warning);
        }
        public static HotelGuestCatalog Load()
        {
            TextAsset asset = Resources.Load<TextAsset>("guest-profiles");
            return FromJsonOrLegacy(asset == null ? null : asset.text);
        }
        public static HotelGuestCatalog FromJsonOrLegacy(string json)
        {
            try { return FromJson(json); }
            catch (InvalidDataException) { return Legacy("Каталог гостей недоступен: используются базовые параметры гостей."); }
        }
        public static HotelGuestCatalog FromJson(string json)
        {
            Check(!string.IsNullOrWhiteSpace(json) && json.Length <= 100000, "Пустой или слишком большой каталог гостей.");
            Document document;
            try { document = JsonUtility.FromJson<Document>(json); }
            catch (ArgumentException error) { throw new InvalidDataException("Неверный JSON каталога гостей.", error); }
            Check(document != null && document.version == 1, "Неподдерживаемая версия каталога гостей.");
            Check(document.profiles != null && document.profiles.Length >= 3 && document.profiles.Length <= 32 &&
                document.requests != null && document.requests.Length > 0 && document.requests.Length <= 32, "Неверные списки каталога.");
            var requests = new HashSet<string>(StringComparer.Ordinal);
            foreach (HotelRequestDefinition request in document.requests)
            {
                Check(request != null && ValidId(request.id) && requests.Add(request.id) && request.kind == "extra_towel", "Повторный ID или неподдерживаемый запрос.");
                Check(InRange(request.delaySeconds, 1, 120) && InRange(request.graceSeconds, 5, 300), "Неверные таймеры запроса.");
            }
            var profiles = new HashSet<string>(StringComparer.Ordinal);
            foreach (HotelGuestProfile profile in document.profiles)
            {
                Check(profile != null && ValidId(profile.id) && profiles.Add(profile.id), "Повторный или неверный ID профиля.");
                Check(!string.IsNullOrWhiteSpace(profile.trait) && profile.trait.Length <= 128 && requests.Contains(profile.requestId ?? ""), "Профиль не связан с запросом.");
                Check(InRange(profile.patienceWarning, 5, 600) && InRange(profile.patienceLimit, 10, 1200) && profile.patienceLimit > profile.patienceWarning,
                    "Неверное терпение гостя.");
                Check(InRange(profile.staySeconds, 60, 400) && InRange(profile.luggageGrace, 5, 300) &&
                    InRange(profile.checkoutWarning, 5, 300) && InRange(profile.checkoutLimit, 10, 600) && profile.checkoutLimit > profile.checkoutWarning,
                    "Неверные таймеры проживания.");
                HotelRequestDefinition request = Array.Find(document.requests, r => r.id == profile.requestId);
                Check(request.delaySeconds < profile.staySeconds, "Запрос начинается после окончания проживания.");
            }
            Check(profiles.Contains("patient") && profiles.Contains("hurried") && profiles.Contains("tidy"), "Нужны профили patient, hurried и tidy.");
            return new HotelGuestCatalog(document);
        }

        public HotelGuestProfile GetProfile(string id)
        {
            HotelGuestProfile p = Array.Find(data.profiles, value => value.id == id);
            return p == null ? null : new HotelGuestProfile { id = p.id, trait = p.trait, requestId = p.requestId,
                patienceWarning = p.patienceWarning, patienceLimit = p.patienceLimit, staySeconds = p.staySeconds,
                luggageGrace = p.luggageGrace, checkoutWarning = p.checkoutWarning, checkoutLimit = p.checkoutLimit };
        }
        public HotelRequestDefinition GetRequest(string id)
        {
            HotelRequestDefinition r = Array.Find(data.requests, value => value.id == id);
            return r == null ? null : new HotelRequestDefinition { id = r.id, kind = r.kind, delaySeconds = r.delaySeconds, graceSeconds = r.graceSeconds };
        }
        internal bool ApplySnapshot(GuestState guest, string profileId)
        {
            if (guest.profileVersion != 0) return false;
            HotelGuestProfile profile = GetProfile(profileId);
            if (profile == null) return false; // Missing profile never rewrites a persisted/legacy guest.
            HotelRequestDefinition request = GetRequest(profile.requestId);
            guest.profileVersion = Version;
            guest.profileId = profile.id; guest.kind = profile.id; guest.trait = profile.trait;
            guest.requestId = request.id; guest.requestKind = request.kind;
            guest.patienceWarning = profile.patienceWarning; guest.patienceLimit = profile.patienceLimit;
            guest.requestDelay = request.delaySeconds; guest.requestGrace = request.graceSeconds;
            guest.stayDuration = profile.staySeconds; guest.luggageGrace = profile.luggageGrace;
            guest.checkoutWarning = profile.checkoutWarning; guest.checkoutLimit = profile.checkoutLimit;
            return true;
        }
        internal static bool InRange(float value, float min, float max)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;
        }
        internal static bool ValidId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 48) return false;
            foreach (char c in id) if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_') return false;
            return true;
        }
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    }
}
