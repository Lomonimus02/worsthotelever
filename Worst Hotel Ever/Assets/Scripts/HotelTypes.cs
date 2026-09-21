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
    }
    [Serializable] public class GuestState
    {
        public int id, room; public string name, kind, trait;
        public string stage = "queue"; // queue, walking, staying, checkout, leaving, gone
        public Vector3 position; public float waited, stay, satisfaction = 100;
        public bool luggageDelivered, towelRequested, compensated, paid;
        public List<string> memories = new List<string>();
    }
    [Serializable] public class ItemState
    {
        public string id, kind; public int ownerGuest; public long holder = -1;
        public Vector3 position; public bool consumed; public int placedRoom;
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
        public List<RoomState> rooms = new List<RoomState>();
        public List<GuestState> guests = new List<GuestState>();
        public List<ItemState> items = new List<ItemState>();
        public List<PlayerState> players = new List<PlayerState>();
        public List<string> reviews = new List<string>();
        public List<string> ledger = new List<string>();
        public string notice = "Подготовьте отель и откройте смену на стойке.";
    }
    [Serializable] public class HotelCommand
    {
        public string action, target; public int number; public Vector3 position; public float yaw, pitch;
        public HotelCommand() { }
        public HotelCommand(string action, string target = "", int number = 0) { this.action = action; this.target = target; this.number = number; }
    }
    public static class HotelLayout
    {
        public static readonly Vector3 Spawn = new Vector3(0, 0.1f, -4.8f);
        public static Vector3 RoomCenter(int n) => new Vector3(n % 2 == 1 ? -4.7f : 4.7f, 0, n <= 102 ? 5.5f : 12.5f);
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
                default: return Door(n) + Vector3.up;
            }
        }
        public static Vector3 Target(string id)
        {
            if (string.IsNullOrEmpty(id)) return Vector3.zero;
            string[] a = id.Split('_');
            if(a.Length == 2 && int.TryParse(a[1], out int n) && n >= 101 && n <=104) return RoomTarget(a[0], n);
            switch(id) {
                case "desk": return new Vector3(0, 1, -.7f);
                case "board": return new Vector3(4.6f, 1.5f, -1.8f);
                case "linen": return new Vector3(-4.8f, 1, -.7f);
                case "towels": return new Vector3(-4.8f, 1, -2.1f);
                case "hamper": return new Vector3(-4.8f, .5f, -3.5f);
                case "bin": return new Vector3(-3.3f, .5f, -3.5f);
                case "tools": return new Vector3(-3.2f, .6f, -.7f);
                default: return Spawn;
            }
        }
    }
}
