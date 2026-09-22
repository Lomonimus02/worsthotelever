using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    [Serializable] public class RoomState
    {
        public int number; public bool outOfService; public int guestId;
        public int bed = 2; // 0: bare, 1: dirty, 2: clean
        public bool towel = true, trash, leak; public float water; public bool upgraded;
        public MvpRoomState mvp;
    }
    [Serializable] public class GuestState
    {
        public int id, room; public string name, kind, trait;
        public string stage = "queue"; // queue, walking, staying, checkout, leaving, gone
        public Vector3 position; public float waited, stay, satisfaction = 100;
        public bool luggageDelivered, towelRequested, compensated, paid;
        public List<string> memories = new List<string>();
        // Immutable-at-arrival balance snapshot. Zero version preserves pre-0.3 guest behavior.
        public int profileVersion; public string profileId, requestId, requestKind;
        public float patienceWarning, patienceLimit, requestDelay, requestGrace, stayDuration;
        public float luggageGrace, checkoutWarning, checkoutLimit, requestElapsed, requestWait;
        public MvpGuestState mvp;
    }
    [Serializable] public class ItemState
    {
        public string id, kind; public int ownerGuest; public long holder = -1;
        public Vector3 position; public bool consumed; public int placedRoom;
        public string size = "small", condition = "clean";
    }
    [Serializable] public class PlayerState
    {
        public ulong id; public Vector3 position; public float yaw, pitch;
        public string held = "", workTarget = ""; public float workProgress, workLastSeen;
    }
    [Serializable] public class HotelState
    {
        public int version = 1, day = 1, cash = 400, earned, expenses, served, nextGuest = 1;
        public string worldId = Guid.NewGuid().ToString("N"), phase = "preparation";
        public float time, dayLength = 480; public int linenStock = 12, towelStock = 12;
        public bool secondToolbox, betterBeds, cartUpgrade; public int arrivals;
        // Additive v1 fields: missing values in older JSON mean no recorded skills and help enabled.
        public int tutorialFlags; public bool tutorialSkipped;
        // Additive save-v1 fields: legacy saves never opt in to pacing or a new catalogue.
        public int contentVersion; public bool guidedOpening;
        public int guidedStage, guidedGuestId, guidedLeakRoom;
        public bool guidedRepairDone, guidedMopDone, dailyLeakIssued;
        public float nextArrivalTime;
        public List<RoomState> rooms = new List<RoomState>();
        public List<GuestState> guests = new List<GuestState>();
        public List<ItemState> items = new List<ItemState>();
        public List<PlayerState> players = new List<PlayerState>();
        public List<string> reviews = new List<string>();
        public List<string> ledger = new List<string>();
        public string notice = "Подготовьте отель и откройте смену на стойке.";
        public MvpHotelState mvp;
    }
    [Serializable] public class HotelCommand
    {
        public string action, target; public int number, guestId; public long sequence; public Vector3 position; public float yaw, pitch;
        public HotelCommand() { }
        public HotelCommand(string action, string target = "", int number = 0) { this.action = action; this.target = target; this.number = number; }
    }
    public static class HotelLayout
    {
        public static readonly Vector3 Spawn = new Vector3(0, 0.1f, -4.8f);
        public const int FirstRoom = 101, LastRoom = 106;
        public const float NorthBoundary = 24;
        public static Vector3 RoomCenter(int n) => new Vector3(n % 2 == 1 ? -4.7f : 4.7f, 0, 5.5f + ((n - 101) / 2) * 7f);
        public static Vector3 Door(int n) => new Vector3(n % 2 == 1 ? -1.5f : 1.5f, 0, RoomCenter(n).z);
        public static Vector3 RoomTarget(string kind, int n)
        {
            Vector3 c = RoomCenter(n); float sign = n % 2 == 1 ? -1 : 1;
            switch (kind) {
                case "bed": return c + new Vector3(sign * 1.1f, .7f, 1.1f);
                case "sink": return c + new Vector3(sign * 1.8f, 1, -1.9f);
                case "water": return c + new Vector3(sign * .8f, .035f, -1.7f);
                case "towel": return c + new Vector3(sign * 1.3f, 1.1f, -2.6f);
                case "trash": return c + new Vector3(-sign * .6f, .3f, -2.5f);
                case "bag": return c + new Vector3(-sign * .5f, .45f, 2.2f);
                case "toilet": return c + new Vector3(sign * 2.2f, .55f, -.55f);
                case "tv": return c + new Vector3(-sign * 1.4f, 1.4f, .3f);
                case "lamp": return c + new Vector3(sign * 2.3f, 1.25f, 2.2f);
                case "coffee": return c + new Vector3(-sign * .6f, .85f, 1.25f);
                case "clean": return c + new Vector3(0, .05f, -.2f);
                case "dirtytowel": return c + new Vector3(sign * 1.3f, .2f, -2.6f);
                default: return Door(n) + Vector3.up;
            }
        }
        public static Vector3 Target(string id)
        {
            if (string.IsNullOrEmpty(id)) return Vector3.zero;
            string[] a = id.Split('_');
            if(a.Length == 2 && int.TryParse(a[1], out int n) && n >= FirstRoom && n <= LastRoom) return RoomTarget(a[0], n);
            switch(id) {
                case "desk": return new Vector3(0, 1, -.7f);
                case "board": return new Vector3(4.6f, 1.5f, -1.8f);
                case "linen": return new Vector3(-4.8f, 1, -.7f);
                case "towels": return new Vector3(-4.8f, 1, -2.1f);
                case "hamper": return new Vector3(-4.8f, .5f, -3.5f);
                case "bin": return new Vector3(-3.3f, .5f, -3.5f);
                case "tools": return new Vector3(-3.2f, .6f, -.7f);
                case "coffee": return new Vector3(6.5f, 1, -.7f);
                case "utility_water": return new Vector3(-7.35f, 1, -5.4f);
                case "utility_power": return new Vector3(-6.25f, 1, -5.4f);
                default: return Spawn;
            }
        }
    }
}
