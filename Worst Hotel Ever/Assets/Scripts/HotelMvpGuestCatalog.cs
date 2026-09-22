using System;
using System.IO;
using UnityEngine;

namespace WorstHotel
{
    [Serializable] public sealed class MvpGuestDefinition
    {
        public string id, name, request;
        public int tariff, partySize, minBed, minTv, preferredBed, preferredTv;
        public bool power;
        public float patience, requestDelay, requestGrace, noiseTolerance, messRate;
    }

    [Serializable] public sealed class MvpTraitDefinition
    {
        public string id, name;
        public float patience, grace, noise, mess;
    }

    // Definitions are copied into each offer, then copied again into the arriving guest.
    // A changed catalogue can never change an already agreed stay.
    public sealed class HotelMvpGuestCatalog
    {
        [Serializable] private sealed class Document
        {
            public int version;
            public MvpGuestDefinition[] archetypes;
            public MvpTraitDefinition[] traits;
        }

        public static readonly string[] ArchetypeIds = { "tourist", "business", "family", "vip" };
        public static readonly string[] TraitIds = { "patient", "impatient", "messy", "demanding", "friendly" };
        private readonly Document data;
        private HotelMvpGuestCatalog(Document document) { data = document; }

        public static HotelMvpGuestCatalog Load()
        {
            TextAsset asset = Resources.Load<TextAsset>("guests-mvp");
            if (asset == null) throw new InvalidDataException("Не найден каталог гостей MVP: guests-mvp.");
            return FromJson(asset.text);
        }

        public static HotelMvpGuestCatalog FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 100000)
                throw new InvalidDataException("Неверный размер каталога гостей MVP.");
            Document d;
            try { d = JsonUtility.FromJson<Document>(json); }
            catch (ArgumentException e) { throw new InvalidDataException("Неверный JSON каталога гостей MVP.", e); }
            Check(d != null && d.version == 2 && d.archetypes != null && d.archetypes.Length == 4 &&
                d.traits != null && d.traits.Length == 5, "Неверная версия или состав каталога гостей MVP.");
            foreach (string id in ArchetypeIds)
            {
                MvpGuestDefinition[] matches = Array.FindAll(d.archetypes, x => x != null && x.id == id);
                Check(matches.Length == 1, "Нет уникального архетипа " + id + ".");
                MvpGuestDefinition a = matches[0];
                Check(!string.IsNullOrEmpty(a.name) && a.name.Length <= 64 && ValidRequest(a.request) &&
                    a.tariff >= 20 && a.tariff <= 1000 && a.partySize == (id == "family" ? 2 : 1) &&
                    a.minBed >= 1 && a.minBed <= 2 && a.minTv >= 0 && a.minTv <= 2 &&
                    a.preferredBed >= a.minBed && a.preferredBed <= 2 && a.preferredTv >= a.minTv && a.preferredTv <= 2 &&
                    Range(a.patience, 60, 600) && Range(a.requestDelay, 5, 300) && Range(a.requestGrace, 20, 300) &&
                    Range(a.noiseTolerance, .1f, 1) && Range(a.messRate, .1f, 3), "Повреждён архетип " + id + ".");
            }
            foreach (string id in TraitIds)
            {
                MvpTraitDefinition[] matches = Array.FindAll(d.traits, x => x != null && x.id == id);
                Check(matches.Length == 1, "Нет уникальной черты " + id + ".");
                MvpTraitDefinition t = matches[0];
                Check(!string.IsNullOrEmpty(t.name) && t.name.Length <= 64 && Range(t.patience, .4f, 2) &&
                    Range(t.grace, .4f, 2) && Range(t.noise, .4f, 2) && Range(t.mess, .4f, 3), "Повреждена черта " + id + ".");
            }
            return new HotelMvpGuestCatalog(d);
        }

        public MvpGuestState Snapshot(string archetype, string trait, string priceMode, int arrivalDay, int nights)
        {
            MvpGuestDefinition a = Array.Find(data.archetypes, x => x.id == archetype);
            MvpTraitDefinition t = Array.Find(data.traits, x => x.id == trait);
            if (a == null || t == null || arrivalDay < 1 || nights < 1 || nights > 3 ||
                (priceMode != "low" && priceMode != "normal" && priceMode != "high"))
                throw new ArgumentException("Неизвестный профиль, цена или срок проживания.");
            float price = priceMode == "low" ? .8f : priceMode == "high" ? 1.35f : 1;
            return new MvpGuestState {
                archetype = archetype, trait = trait, partySize = a.partySize,
                arrivalDay = arrivalDay, departureDay = arrivalDay + nights, billableNights = nights,
                agreedTariff = Mathf.RoundToInt(a.tariff * price), minBedQuality = a.minBed, minTvQuality = a.minTv,
                preferredBedQuality = priceMode == "high" || trait == "demanding" ? 2 : a.preferredBed,
                preferredTvQuality = trait == "demanding" ? Math.Max(1, a.preferredTv) : a.preferredTv,
                requiresWater = true, requiresPower = a.power, patience = a.patience * t.patience,
                requestDelay = a.requestDelay, requestGrace = a.requestGrace * t.grace,
                noiseTolerance = Mathf.Clamp(a.noiseTolerance * t.noise, .1f, 1), messRate = a.messRate * t.mess,
                preferredRequest = trait == "messy" ? "cleaning" : a.request
            };
        }

        public static MvpGuestState Copy(MvpGuestState c)
        {
            if (c == null) return null;
            return new MvpGuestState {
                archetype = c.archetype, trait = c.trait, partyId = c.partyId, groupId = c.groupId,
                reservationId = c.reservationId, partySize = c.partySize, arrivalDay = c.arrivalDay,
                departureDay = c.departureDay, agreedTariff = c.agreedTariff, billableNights = c.billableNights,
                minBedQuality = c.minBedQuality, minTvQuality = c.minTvQuality,
                preferredBedQuality = c.preferredBedQuality, preferredTvQuality = c.preferredTvQuality,
                requiresWater = c.requiresWater, requiresPower = c.requiresPower,
                reviewRecorded = c.reviewRecorded, requestIssued = c.requestIssued, patience = c.patience,
                requestDelay = c.requestDelay, requestGrace = c.requestGrace, noiseTolerance = c.noiseTolerance,
                messRate = c.messRate, serviceElapsed = c.serviceElapsed, blockedTime = c.blockedTime,
                preferredRequest = c.preferredRequest
            };
        }

        public static string ArchetypeName(string id)
        {
            switch (id) { case "business": return "Деловой гость"; case "family": return "Семья · 2 человека";
                case "vip": return "VIP"; default: return "Турист"; }
        }
        public static string TraitName(string id)
        {
            switch (id) { case "impatient": return "Нетерпеливый"; case "messy": return "Неряшливый";
                case "demanding": return "Требовательный"; case "friendly": return "Дружелюбный"; default: return "Терпеливый"; }
        }
        public static bool ValidRequest(string kind) { return kind == "towel" || kind == "coffee" || kind == "cleaning" || kind == "luggage"; }
        private static bool Range(float n, float min, float max) { return !float.IsNaN(n) && !float.IsInfinity(n) && n >= min && n <= max; }
        private static void Check(bool valid, string message) { if (!valid) throw new InvalidDataException(message); }
    }
}
