using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    // Durable crew slots are independent of disposable NGO connection IDs.
    [Serializable] public sealed class HotelCrewState
    {
        public int slot;
        public bool joined;
        public string life = "healthy", lastHit = "";
        public float health = 100, bleedout, shield;
        public Vector3 position;
        public int downs, rescues;
    }
    [Serializable] public sealed class HotelIncidentState
    {
        public int room;
        public string kind, status = "planned"; // electric, steam, fumes; planned/warning/active/resolved
        public bool isolated;
        public float triggerAt, warningRemaining, activeSeconds;
    }
    [Serializable] public sealed class HotelDangerState
    {
        // Explicit marker; JsonUtility may materialize missing inline objects at CLR defaults.
        public int schema;
        public int day, serial, wins, losses, streak, bestStreak;
        public string mode = "standard", status = "briefing", outcome = "", reason = "";
        public float elapsed, safety = 100, criticalRemaining = 30, graceUntil;
        public int requiredIncidents, requiredService, resolved, servicePoints;
        public float minimumSeconds;
        public int reward, penalty, paidReward, chargedPenalty, medkits = 3, selfRescues = 1;
        public bool settled;
        public List<HotelCrewState> crew = new List<HotelCrewState>();
        public List<HotelIncidentState> incidents = new List<HotelIncidentState>();
    }
}
