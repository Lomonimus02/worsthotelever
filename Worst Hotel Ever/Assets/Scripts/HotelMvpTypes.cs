using System;
using System.Collections.Generic;

namespace WorstHotel
{
    // Authoritative, additive save-v2 DTOs. UI and world projections never mutate them.
    [Serializable] public sealed class MvpRoomState
    {
        public bool owned = true;
        public int capacity = 1, bedQuality = 1, tvQuality = 1, dirtyTowels;
        public string finishId = "original";
        public float dirt, binFill, noise;
        public List<MvpEquipmentState> equipment = new List<MvpEquipmentState>();
    }
    [Serializable] public sealed class MvpEquipmentState
    {
        public string kind;
        public bool installed = true, localFault;
        public int quality = 1, episode;
        public float wear;
    }
    [Serializable] public sealed class MvpUtilityState
    {
        public bool waterFault, powerFault;
        public int waterEpisode, powerEpisode;
        public float waterWear, powerWear;
    }
    [Serializable] public sealed class MvpGuestState
    {
        public string archetype = "tourist", trait = "patient", partyId = "", groupId = "", reservationId = "";
        public int partySize = 1, arrivalDay = 1, departureDay = 2, agreedTariff = 100, billableNights = 1;
        public int minBedQuality = 1, minTvQuality, preferredBedQuality = 1, preferredTvQuality;
        public bool requiresWater = true, requiresPower, reviewRecorded, requestIssued;
        public float patience = 180, requestDelay = 45, requestGrace = 100, noiseTolerance = .6f;
        public float messRate = 1, serviceElapsed, blockedTime;
        public string preferredRequest = "towel";
    }
    [Serializable] public sealed class MvpRequestState
    {
        public string id, kind, target, status = "open";
        public int guestId;
        public float createdAt, dueAt;
        public bool rewarded;
    }
    [Serializable] public sealed class MvpComplaintState
    {
        public string id, category, causeKey, status = "active";
        public int guestId, escalation;
        public float exposure, recoveredAt;
        public bool recovered;
    }
    [Serializable] public sealed class MvpReservationState
    {
        public string id, source = "booking", status = "confirmed", partyId = "", groupId = "";
        public int room, arrivalDay, departureDay, guestId;
        public float arrivalTime;
        public MvpGuestState contract;
    }
    [Serializable] public sealed class MvpScheduleState
    {
        public string id, reservationId = "", source = "walkin", status = "planned", groupId = "";
        public int day, guestId;
        public float time;
        public MvpGuestState contract;
    }
    [Serializable] public sealed class MvpGroupState
    {
        public string id, source = "planned", status = "expected";
        public int arrivalDay, admitted, refused, completed;
        public List<string> reservationIds = new List<string>();
    }
    [Serializable] public sealed class MvpReviewState
    {
        public int guestId, day, stars;
        public string text;
    }
    [Serializable] public sealed class MvpCooldownState
    {
        public string id;
        public float until;
    }
    [Serializable] public sealed class MvpEventState
    {
        public string id, kind, category, status = "active";
        public float startedAt, until;
        public int room;
    }
    [Serializable] public sealed class MvpDirectorState
    {
        public float nextDecision = 180, globalCooldown, workload, stress, recoveryUntil;
        public string band = "low";
        public List<MvpCooldownState> cooldowns = new List<MvpCooldownState>();
        public List<string> recent = new List<string>();
        public List<MvpEventState> events = new List<MvpEventState>();
    }
    [Serializable] public sealed class MvpPingState
    {
        public string target;
        public ulong playerId;
        public float until;
    }
    [Serializable] public sealed class MvpHotelState
    {
        public string priceMode = "normal";
        public int rngVersion = 1, rngState = 1597463007, rngDraws, nextId = 1, preparedDay;
        public float elapsed, reputation = 3;
        public int coffeeStock = 12;
        public bool betterLinen, coffeeMachine;
        public MvpUtilityState utilities = new MvpUtilityState();
        public MvpDirectorState director = new MvpDirectorState();
        public List<MvpReservationState> reservations = new List<MvpReservationState>();
        public List<MvpScheduleState> schedule = new List<MvpScheduleState>();
        public List<MvpGroupState> groups = new List<MvpGroupState>();
        public List<MvpRequestState> requests = new List<MvpRequestState>();
        public List<MvpComplaintState> complaints = new List<MvpComplaintState>();
        public List<MvpReviewState> reviews = new List<MvpReviewState>();
        public List<MvpPingState> pings = new List<MvpPingState>();
    }
}
