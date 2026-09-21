using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorstHotel
{
    /// <summary>Authoritative interaction identity, also copied to fixture colliders.</summary>
    public sealed class HotelTarget : MonoBehaviour
    {
        public string id;
        public string label;
    }

    /// <summary>
    /// Original, runtime-built first floor. Layout coordinates come only from HotelLayout.
    /// Build is idempotent. Apply reconciles cached visuals; it never rebuilds the hotel.
    /// No camera, simulation, input, networking, packages or scene settings are owned here.
    /// </summary>
    public sealed class HotelWorld : MonoBehaviour
    {
        enum Ink { Cream, Plaster, Wood, Oak, Burgundy, Red, Brass, White, Linen, Dirty,
            Dark, Metal, Ceramic, Water, Foam, Teal, Green, Leaf, Gold, Blue, Skin, SkinDark, Hair, Glow, Paper }
        static readonly Dictionary<Ink, Material> Materials = new Dictionary<Ink, Material>();
        static Mesh cube, bevel, sphere, cylinder, cone;
        static Font font;
        static Material fontMaterial;
        Transform hotelRoot, architecture, living;
        bool built;
        readonly Dictionary<int, RoomVisual> rooms = new Dictionary<int, RoomVisual>();
        readonly Dictionary<int, PersonVisual> guests = new Dictionary<int, PersonVisual>();
        readonly Dictionary<ulong, PersonVisual> players = new Dictionary<ulong, PersonVisual>();
        readonly Dictionary<string, ItemVisual> items = new Dictionary<string, ItemVisual>();
        readonly HashSet<int> seenGuests = new HashSet<int>();
        readonly HashSet<ulong> seenPlayers = new HashSet<ulong>();
        readonly HashSet<string> seenItems = new HashSet<string>();
        readonly Dictionary<ulong, PlayerState> playerStates = new Dictionary<ulong, PlayerState>();
        readonly List<int> removeGuests = new List<int>();
        readonly List<ulong> removePlayers = new List<ulong>();
        readonly List<string> removeItems = new List<string>();
        readonly List<GameObject> linenPiles = new List<GameObject>();
        readonly List<GameObject> towelPiles = new List<GameObject>();
        TextMesh linenCount, towelCount, openSign;
        int previousLinen = -1, previousTowels = -1;
        string previousPhase;

        sealed class RoomVisual
        {
            public GameObject clean, dirty, towel, trash, leak, upgraded;
            public Transform water;
            public Renderer lamp;
            public TextMesh status;
            public int bed = -1, flag = -1;
            public float waterAmount = -1;
        }
        sealed class PersonVisual
        {
            public GameObject root;
            public HotelFigure figure;
            public TextMesh caption;
            public string previousCaption;
        }
        sealed class ItemVisual
        {
            public GameObject root;
            public Collider[] colliders;
            public string kind;
            public bool held;
            public TextMesh ownerTag;
            public int ownerGuest = -1;
        }

        public void Build()
        {
            if (built) return;
            EnsureAssets();
            hotelRoot = Group(transform, "GRAND HOTEL / original procedural world");
            architecture = Group(hotelRoot, "Architecture and fixtures");
            living = Group(hotelRoot, "State and people");
            BuildShell();
            BuildLobby();
            BuildStorage();
            for (int n = 101; n <= 104; ++n) BuildRoom(n);
            BuildLighting();
            // Only immutable architecture is batched. Water, bed variants, stock and people stay movable.
            StaticBatchingUtility.Combine(architecture.gameObject);
            built = true;
        }

        public void Apply(HotelState state, ulong localPlayerId)
        {
            if (state == null) return;
            if (!built) Build();
            ApplyStock(state);
            if (state.rooms != null)
                foreach (RoomState room in state.rooms)
                    if (room != null && rooms.TryGetValue(room.number, out RoomVisual visual)) ApplyRoom(room, visual);

            seenPlayers.Clear(); playerStates.Clear();
            if (state.players != null)
                foreach (PlayerState player in state.players)
                {
                    if (player == null || !seenPlayers.Add(player.id)) continue;
                    playerStates[player.id] = player;
                    if (!players.TryGetValue(player.id, out PersonVisual visual))
                    {
                        visual = CreatePerson(true, unchecked((int)player.id), "player_" + player.id, "Сотрудник");
                        players.Add(player.id, visual);
                    }
                    SetActive(visual.root, player.id != localPlayerId);
                    visual.figure.Pose(player.position, player.yaw, player.pitch,
                        !string.IsNullOrEmpty(player.held), !string.IsNullOrEmpty(player.workTarget));
                }
            removePlayers.Clear();
            foreach (var entry in players) if (!seenPlayers.Contains(entry.Key)) removePlayers.Add(entry.Key);
            foreach (ulong id in removePlayers) { Retire(players[id].root); players.Remove(id); }

            seenGuests.Clear();
            if (state.guests != null)
                foreach (GuestState guest in state.guests)
                {
                    if (guest == null || guest.stage == "gone" || !seenGuests.Add(guest.id)) continue;
                    if (!guests.TryGetValue(guest.id, out PersonVisual visual))
                    {
                        visual = CreatePerson(false, guest.id, "guest_" + guest.id, "Гость");
                        guests.Add(guest.id, visual);
                    }
                    visual.figure.Pose(guest.position, null, 0, false, false);
                    visual.figure.annoyed = guest.satisfaction < 45;
                    string caption = GuestCaption(guest);
                    if (caption != visual.previousCaption)
                    {
                        visual.caption.text = caption;
                        visual.caption.color = guest.satisfaction < 45 ? ColorOf(Ink.Red) : ColorOf(Ink.Dark);
                        visual.previousCaption = caption;
                        SetTargetLabel(visual.root, "Гость: " + (guest.name ?? ("№" + guest.id)));
                    }
                }
            removeGuests.Clear();
            foreach (var entry in guests) if (!seenGuests.Contains(entry.Key)) removeGuests.Add(entry.Key);
            foreach (int id in removeGuests) { Retire(guests[id].root); guests.Remove(id); }

            seenItems.Clear();
            ItemState cartState = state.items == null ? null : state.items.Find(i => i != null && !i.consumed && i.kind == "cart");
            int cargoSlot = 0;
            if (state.items != null)
                foreach (ItemState item in state.items)
                {
                    if (item == null || item.consumed || string.IsNullOrEmpty(item.id) || !seenItems.Add(item.id)) continue;
                    if (!items.TryGetValue(item.id, out ItemVisual visual) || visual.kind != item.kind)
                    {
                        if (visual != null) Retire(visual.root);
                        GameObject root = MakeItem(item.kind);
                        root.name = item.id + " / " + item.kind;
                        root.transform.SetParent(living, false);
                        Target(root, item.id, ItemLabel(item.kind));
                        visual = new ItemVisual { root = root, colliders = root.GetComponentsInChildren<Collider>(true), kind = item.kind };
                        if (item.kind == "bag")
                            visual.ownerTag = Text(root.transform, "", new Vector3(0, .4f, -.218f), .065f, Ink.White);
                        items[item.id] = visual;
                    }
                    if (visual.ownerTag != null && visual.ownerGuest != item.ownerGuest)
                    {
                        GuestState owner = state.guests == null ? null : state.guests.Find(g => g != null && g.id == item.ownerGuest);
                        string ownerName = owner == null ? "№" + item.ownerGuest : owner.name;
                        visual.ownerTag.text = ownerName;
                        SetTargetLabel(visual.root, "Чемодан: " + ownerName);
                        visual.ownerGuest = item.ownerGuest;
                    }
                    bool cargo = item.placedRoom == -1 && cartState != null;
                    long carrier = cargo ? cartState.holder : item.holder;
                    bool held = carrier >= 0;
                    bool local = held && (ulong)carrier == localPlayerId;
                    int slot = cargo ? cargoSlot++ : 0;
                    SetActive(visual.root, !local);
                    if (visual.held != held)
                    {
                        foreach (Collider c in visual.colliders) c.enabled = !held;
                        visual.held = held;
                    }
                    if (local) continue;
                    if (held && playerStates.TryGetValue((ulong)carrier, out PlayerState holder))
                    {
                        Quaternion rotation = Quaternion.Euler(0, holder.yaw, 0);
                        Vector3 offset = new Vector3(.34f, .94f, .44f);
                        if (cargo) offset += new Vector3(slot == 0 ? -.23f : .23f, .31f, 0);
                        visual.root.transform.SetPositionAndRotation(holder.position + rotation * offset, rotation);
                    }
                    else
                    {
                        // State uses a generic interaction height (.25 after drop). The model pivot
                        // is its foot, so rest it on the floor or on its documented luggage stand.
                        Vector3 at = item.position;
                        at.y = item.placedRoom > 0 ? HotelLayout.RoomTarget("bag", item.placedRoom).y + .015f : item.placedRoom == -1 ? .325f : .025f;
                        visual.root.transform.SetPositionAndRotation(at, Quaternion.identity);
                    }
                }
            removeItems.Clear();
            foreach (var entry in items) if (!seenItems.Contains(entry.Key)) removeItems.Add(entry.Key);
            foreach (string id in removeItems) { Retire(items[id].root); items.Remove(id); }
        }

        void BuildShell()
        {
            Transform p = Group(architecture, "Shell / 1.50 m open room portals");
            Box(p, "Lobby oak floor", new Vector3(0, -.14f, -2.5f), new Vector3(15.8f, .28f, 9), Ink.Oak, true);
            for (int x = -7; x <= 7; ++x)
                Box(p, "Long floorboard joint", new Vector3(x, .003f, -2.5f), new Vector3(.016f, .005f, 8.9f), Ink.Wood);
            for (int z = -6; z <= 1; z += 2)
                for (int x = -7; x <= 7; ++x)
                    Box(p, "Staggered floorboard end", new Vector3(x + .48f, .004f, z + (x % 2) * .65f), new Vector3(.94f, .006f, .014f), Ink.Wood);
            Box(p, "Hall floor", new Vector3(0, -.12f, 9), new Vector3(3, .24f, 14), Ink.Oak, true);
            Rug(p, new Vector3(0, .02f, 8.95f), new Vector2(2.1f, 13.6f));
            for (int z = 3; z < 16; z += 2)
            {
                GameObject diamond = Box(p, "Runner diamond", new Vector3(0, .039f, z), new Vector3(.22f, .008f, .22f), Ink.Brass);
                diamond.transform.localRotation = Quaternion.Euler(0, 45, 0);
            }
            Wall(p, new Vector3(-7.9f, 0, 4.5f), new Vector3(.2f, 3.6f, 23));
            Wall(p, new Vector3(7.9f, 0, 4.5f), new Vector3(.2f, 3.6f, 23));
            Wall(p, new Vector3(0, 0, 16.1f), new Vector3(16, 3.6f, .2f));
            Wall(p, new Vector3(-4.95f, 0, -7), new Vector3(5.9f, 3.6f, .2f));
            Wall(p, new Vector3(4.95f, 0, -7), new Vector3(5.9f, 3.6f, .2f));
            Box(p, "Entrance lintel", new Vector3(0, 3.17f, -7), new Vector3(4, .86f, .24f), Ink.Wood, true);
            // An open porch is part of the navigable floor, with a physical boundary beyond it.
            Box(p, "Porch", new Vector3(0, -.16f, -8.25f), new Vector3(8, .32f, 2.5f), Ink.Ceramic, true);
            Box(p, "Porch back rail", new Vector3(0, .5f, -9.55f), new Vector3(8.1f, 1, .16f), Ink.Wood, true);
            Box(p, "Porch left rail", new Vector3(-4.05f, .5f, -8.25f), new Vector3(.16f, 1, 2.6f), Ink.Wood, true);
            Box(p, "Porch right rail", new Vector3(4.05f, .5f, -8.25f), new Vector3(.16f, 1, 2.6f), Ink.Wood, true);
            Box(p, "Lobby ceiling", new Vector3(0, 3.65f, -2.5f), new Vector3(15.8f, .18f, 9), Ink.Cream, true);
            Box(p, "Hall ceiling", new Vector3(0, 3.65f, 9), new Vector3(3, .18f, 14), Ink.Cream, true);
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Wall(p, new Vector3(sign * 4.7f, 0, 2), new Vector3(6.4f, 3.6f, .18f));
                Wall(p, new Vector3(sign * 4.7f, 0, 9), new Vector3(6.4f, 3.6f, .18f));
                foreach (float z in new[] { 3.375f, 9f, 14.625f })
                    Wall(p, new Vector3(sign * 1.5f, 0, z), new Vector3(.18f, 3.6f, z == 9 ? 5.5f : 2.75f));
                foreach (float z in new[] { 5.5f, 12.5f })
                    Box(p, "Portal lintel", new Vector3(sign * 1.5f, 3.12f, z), new Vector3(.24f, .96f, 1.5f), Ink.Plaster, true);
                for (int z = 3; z <= 15; z += 7)
                {
                    Frame(p, new Vector3(sign * 1.387f, 2.05f, z + .5f), sign * 90, z == 3 ? "ТИШЕ ЕДЕШЬ" : "СЛАДКИХ СНОВ", .83f, .76f);
                }
            }
            Box(p, "Future elevator surround", new Vector3(0, 1.36f, 15.95f), new Vector3(2.2f, 2.72f, .18f), Ink.Wood, true);
            Box(p, "Future lift left", new Vector3(-.47f, 1.35f, 15.83f), new Vector3(.91f, 2.45f, .08f), Ink.Metal);
            Box(p, "Future lift right", new Vector3(.47f, 1.35f, 15.83f), new Vector3(.91f, 2.45f, .08f), Ink.Metal);
            Text(p, "КОГДА-НИБУДЬ: ЭТАЖ 2", new Vector3(0, 2.99f, 15.79f), .092f, Ink.Dark);
            Text(p, "105 / 106\nСКОРО. НАВЕРНОЕ.", new Vector3(0, 1.63f, 15.74f), .12f, Ink.Cream);
            Box(p, "Lift caution stripe", new Vector3(0, .9f, 15.71f), new Vector3(1.93f, .13f, .025f), Ink.Gold);
        }

        void BuildLobby()
        {
            Transform p = Group(architecture, "Reception and lounge");
            Transform desk = Group(p, "Reception / desk", HotelLayout.Target("desk"));
            Rounded(desk, "Reception body", new Vector3(0, -.52f, 0), new Vector3(2.4f, .92f, .74f), Ink.Wood, true);
            Rounded(desk, "Worn oak counter", new Vector3(0, .025f, 0), new Vector3(2.6f, .16f, .88f), Ink.Oak, true);
            for (int i = -1; i <= 1; ++i)
            {
                Box(desk, "Burgundy recessed panel", new Vector3(i * .73f, -.47f, -.378f), new Vector3(.64f, .61f, .03f), Ink.Burgundy);
                Box(desk, "Panel gilt line", new Vector3(i * .73f, -.19f, -.399f), new Vector3(.49f, .017f, .018f), Ink.Brass);
            }
            Text(desk, "Г Р А Н Д", new Vector3(0, -.41f, -.407f), .12f, Ink.Cream);
            Bell(desk, new Vector3(.8f, .14f, -.14f));
            Rounded(desk, "Guest register", new Vector3(-.55f, .15f, -.12f), new Vector3(.55f, .07f, .36f), Ink.Burgundy);
            Box(desk, "Register pages", new Vector3(-.55f, .195f, -.12f), new Vector3(.49f, .018f, .32f), Ink.Paper);
            Tube(desk, "Pen", new Vector3(-.7f, .215f, -.22f), new Vector3(-.43f, .215f, -.05f), .023f, Ink.Brass);
            Target(desk.gameObject, "desk", "Ресепшен — смена и гости");
            Box(p, "Hanging hotel sign", new Vector3(0, 2.95f, 1.62f), new Vector3(2.8f, .68f, .17f), Ink.Burgundy);
            for (int s = -1; s <= 1; s += 2)
                Tube(p, "Sign chain", new Vector3(s * 1.1f, 3.3f, 1.62f), new Vector3(s * 1.1f, 3.57f, 1.62f), .035f, Ink.Brass);
            Text(p, "ГРАНД ОТЕЛЬ", new Vector3(0, 3.04f, 1.52f), .21f, Ink.Cream);
            Text(p, "ПЯТЬ ЗВЁЗД. ПОКА ОДНА.", new Vector3(0, 2.79f, 1.52f), .072f, Ink.Brass);
            openSign = Text(living, "ГОТОВИМСЯ К СМЕНЕ", new Vector3(0, 1.26f, -.95f), .064f, Ink.Dark);
            Box(p, "Desk tent card", new Vector3(0, 1.27f, -.92f), new Vector3(.94f, .26f, .035f), Ink.Paper);
            Rug(p, new Vector3(0, .019f, -4.75f), new Vector2(2.8f, 1.65f));
            Text(p, "ДОБРО ПОЖАЛОВАТЬ", new Vector3(0, 2.86f, -6.85f), .19f, Ink.Cream, 180);

            Transform board = Group(p, "Shift board", HotelLayout.Target("board"));
            Rounded(board, "Board timber frame", Vector3.zero, new Vector3(2.15f, 1.62f, .16f), Ink.Wood, true);
            Box(board, "Board cork", new Vector3(0, 0, -.096f), new Vector3(1.99f, 1.46f, .045f), Ink.Oak);
            for (int s = -1; s <= 1; s += 2)
                Box(board, "Board stand", new Vector3(s * .86f, -.86f, .08f), new Vector3(.1f, 1.3f, .35f), Ink.Wood, true);
            Text(board, "ДЕЛА ОТЕЛЯ", new Vector3(0, .55f, -.133f), .17f, Ink.Cream);
            string[] notes = { "ГОСТИ\nЗаселить\nПомочь\nПроводить", "НОМЕРА\nПостель\nПолотенца\nПорядок", "ПОМНИТЬ\nУлыбаться\nДаже если\nснова течёт" };
            for (int i = 0; i < 3; ++i)
            {
                Box(board, "Pinned note", new Vector3((i - 1) * .62f, -.11f, -.134f), new Vector3(.54f, .77f, .012f), i == 2 ? Ink.Linen : Ink.Paper);
                Text(board, notes[i], new Vector3((i - 1) * .62f, -.08f, -.15f), .075f, Ink.Dark);
                Ball(board, "Drawing pin", new Vector3((i - 1) * .62f, .21f, -.158f), new Vector3(.065f, .065f, .035f), Ink.Burgundy);
            }
            Target(board.gameObject, "board", "Доска задач и улучшений");
            Sofa(p, new Vector3(5.9f, 0, -5.55f), 180);
            Transform coffee = Group(p, "Crooked coffee table", new Vector3(5.65f, 0, -4.05f));
            Rounded(coffee, "Tabletop", new Vector3(0, .56f, 0), new Vector3(1.3f, .14f, .75f), Ink.Oak, true);
            for (int s = -1; s <= 1; s += 2)
                Tube(coffee, "Splayed table leg", new Vector3(s * .49f, .52f, 0), new Vector3(s * .58f, .05f, 0), .11f, Ink.Wood);
            Cup(coffee, new Vector3(.26f, .66f, 0));
            Box(coffee, "Old magazine", new Vector3(-.27f, .64f, -.02f), new Vector3(.37f, .025f, .25f), Ink.Teal);
            Plant(p, new Vector3(7.04f, 0, .9f), 1.22f);
            Plant(p, new Vector3(2.6f, 0, -6.1f), .92f);
            Frame(p, new Vector3(6.2f, 2.1f, 1.875f), 0, "ОТДЫХ — ЭТО РАБОТА.\nНО НЕ ВАША.", 1.7f, .95f);
            Clock(p, new Vector3(3.1f, 2.58f, 1.87f));
            Lamp(p, new Vector3(4.6f, 2.98f, -3.6f), .55f);
            Lamp(p, new Vector3(0, 3.13f, -3.3f), .67f);
        }

        void BuildStorage()
        {
            Transform p = Group(architecture, "Housekeeping storage");
            Wall(p, new Vector3(-2.1f, 0, -5.8f), new Vector3(.16f, 3.6f, 2.4f));
            Wall(p, new Vector3(-2.1f, 0, -.3f), new Vector3(.16f, 3.6f, 4.6f));
            Box(p, "Storage lintel", new Vector3(-2.1f, 3.1f, -3.6f), new Vector3(.2f, 1, 2), Ink.Plaster, true);
            Text(p, "СКЛАД", new Vector3(-1.99f, 2.86f, -3.6f), .22f, Ink.Burgundy, -90);
            Text(p, "101–104  →", new Vector3(-1.99f, 2.31f, -.9f), .15f, Ink.Dark, -90);
            foreach (string id in new[] { "linen", "towels" })
            {
                Vector3 at = HotelLayout.Target(id);
                Transform rack = Group(p, "Supply rack / " + id, at);
                Box(rack, "Rack backing", new Vector3(-.5f, .1f, 0), new Vector3(.08f, 2.1f, 1.14f), Ink.Wood, true);
                for (int j = -1; j <= 1; ++j)
                    Box(rack, "Oak shelf", new Vector3(0, j * .63f - .18f, 0), new Vector3(1.1f, .09f, 1.14f), Ink.Oak, true);
                for (int s = -1; s <= 1; s += 2)
                    Box(rack, "Rack upright", new Vector3(.45f, .05f, s * .54f), new Vector3(.075f, 2.1f, .075f), Ink.Brass, true);
                Target(rack.gameObject, id, id == "linen" ? "Чистое постельное бельё" : "Чистые полотенца");
                Text(p, id == "linen" ? "БЕЛЬЁ" : "ПОЛОТЕНЦА", at + new Vector3(.57f, .95f, 0), .115f, Ink.Cream, -90);
                TextMesh stock = Text(living, "", at + new Vector3(.575f, .28f, 0), .095f, Ink.Dark, -90);
                if (id == "linen") linenCount = stock; else towelCount = stock;
                Box(rack, "Stock label backing", new Vector3(.559f, .28f, 0), new Vector3(.018f, .2f, .69f), Ink.Paper);
                for (int j = 0; j < 6; ++j)
                {
                    GameObject supply = MakeItem(id == "linen" ? "linen" : "towel");
                    supply.name = "Supply stock / " + id + " " + j;
                    supply.transform.SetParent(living, false);
                    supply.transform.localPosition = at + new Vector3(0, -.73f + (j / 2) * .63f, (j % 2 == 0 ? -.28f : .28f));
                    supply.transform.localScale = Vector3.one * .62f;
                    Target(supply, id, id == "linen" ? "Взять чистое бельё" : "Взять полотенце");
                    (id == "linen" ? linenPiles : towelPiles).Add(supply);
                }
            }
            Transform hamper = Group(p, "Laundry hamper", HotelLayout.Target("hamper"));
            Rounded(hamper, "Wicker body", new Vector3(0, -.04f, 0), new Vector3(.8f, .83f, .78f), Ink.Oak, true);
            for (int i = 0; i < 7; ++i)
                Box(hamper, "Wicker weave", new Vector3(0, -.37f + i * .115f, -.401f), new Vector3(.73f, .025f, .018f), Ink.Wood);
            Rounded(hamper, "Hamper dark opening", new Vector3(0, .39f, 0), new Vector3(.67f, .035f, .64f), Ink.Dark);
            Text(hamper, "ГРЯЗНОЕ\nБЕЛЬЁ", new Vector3(0, .01f, -.424f), .084f, Ink.Cream);
            Target(hamper.gameObject, "hamper", "Корзина для грязного белья");
            Transform bin = Group(p, "Waste sorting bin", HotelLayout.Target("bin"));
            Cylinder(bin, "Bin body", new Vector3(0, -.05f, 0), new Vector3(.75f, .89f, .75f), Ink.Teal, true);
            Cylinder(bin, "Bin lid", new Vector3(0, .42f, 0), new Vector3(.83f, .1f, .83f), Ink.Dark);
            Text(bin, "МУСОР", new Vector3(0, .07f, -.389f), .08f, Ink.Cream);
            Target(bin.gameObject, "bin", "Выбросить мешок мусора");
            Frame(p, new Vector3(-6.4f, 1.8f, 1.87f), 0, "ЧИСТОТА\nСАМА СЕБЯ\nНЕ СДЕЛАЕТ", 1.4f, 1.1f);
            // Tools spawn on HotelLayout.Target("tools"); leave this floor patch unobstructed.
            Box(p, "Tool parking mat", new Vector3(-3.2f, .008f, -.7f), new Vector3(1.05f, .015f, 1.12f), Ink.Dark);
            Text(p, "ИНСТРУМЕНТЫ", new Vector3(-3.2f, 1.4f, 1.88f), .105f, Ink.Cream);
            Lamp(p, new Vector3(-4.8f, 3.05f, -2.7f), .5f);
        }

        void BuildRoom(int n)
        {
            Vector3 c = HotelLayout.RoomCenter(n);
            float s = n % 2 == 1 ? -1 : 1;
            Transform p = Group(architecture, "Room " + n);
            Transform dynamicRoom = Group(living, "Room " + n + " / conditions");
            RoomVisual visual = new RoomVisual(); rooms.Add(n, visual);
            Box(p, "Bedroom floor", c + new Vector3(0, -.12f, 0), new Vector3(6.25f, .24f, 6.85f), Ink.Oak, true);
            Box(p, "Bedroom ceiling", c + new Vector3(0, 3.65f, 0), new Vector3(6.25f, .18f, 6.85f), Ink.Cream, true);
            for (int i = 0; i < 8; ++i)
                Box(p, "Bedroom floorboard seam", c + new Vector3(s * (-2.7f + i * .76f), .002f, 0), new Vector3(.014f, .004f, 6.7f), Ink.Wood);
            Rug(p, c + new Vector3(s * .35f, .014f, 1.16f), new Vector2(3.5f, 2.2f));
            for (int x = 0; x < 5; ++x)
                for (int z = 0; z < 3; ++z)
                    Box(p, "Washroom tile", c + new Vector3(s * (-1.8f + x * .91f), .01f, -2.98f + z * .64f),
                        new Vector3(.88f, .016f, .61f), (x + z) % 2 == 0 ? Ink.Ceramic : Ink.Cream);
            // Bed runs east/west. Its nearest edge is z = centre + .36, leaving the NPC centreline clear.
            Vector3 bedAt = HotelLayout.RoomTarget("bed", n);
            Transform bed = Group(p, "Bed " + n, bedAt);
            Rounded(bed, "Timber bed frame", new Vector3(0, -.34f, 0), new Vector3(2.65f, .28f, 1.47f), Ink.Wood, true);
            Rounded(bed, "Mattress ticking", new Vector3(0, -.1f, 0), new Vector3(2.5f, .29f, 1.42f), Ink.Linen, true);
            Rounded(bed, "Scalloped headboard", new Vector3(s * 1.24f, .02f, 0), new Vector3(.16f, 1.2f, 1.54f), Ink.Wood, true);
            for (int j = -1; j <= 1; ++j)
                Rounded(bed, "Upholstered headboard inset", new Vector3(s * 1.135f, .2f, j * .44f), new Vector3(.052f, .57f, .34f), Ink.Burgundy);
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    Cylinder(bed, "Turned bed foot", new Vector3(x * 1.08f, -.55f, z * .58f), new Vector3(.13f, .3f, .13f), Ink.Wood);
            Target(bed.gameObject, "bed_" + n, "Кровать — снять / застелить бельё");
            Transform clean = Group(dynamicRoom, "Clean bedding", bedAt);
            Rounded(clean, "Clean duvet", new Vector3(-s * .17f, .075f, 0), new Vector3(2.1f, .19f, 1.4f), Ink.White);
            Rounded(clean, "Burgundy runner", new Vector3(-s * .78f, .185f, 0), new Vector3(.43f, .045f, 1.43f), Ink.Burgundy);
            for (int z = -1; z <= 1; z += 2)
                Rounded(clean, "Plump pillow", new Vector3(s * .9f, .125f, z * .35f), new Vector3(.5f, .24f, .57f), Ink.White);
            visual.clean = clean.gameObject;
            Transform dirty = Group(dynamicRoom, "Dirty bedding", bedAt);
            Rounded(dirty, "Crumpled sheet", new Vector3(-s * .21f, .085f, .01f), new Vector3(1.99f, .18f, 1.36f), Ink.Dirty);
            for (int j = 0; j < 5; ++j)
            {
                GameObject fold = Rounded(dirty, "Rumpled blanket fold", new Vector3(-.77f + j * .35f, .2f, .05f), new Vector3(.33f, .18f, 1.12f), j % 2 == 0 ? Ink.Linen : Ink.Dirty);
                fold.transform.localRotation = Quaternion.Euler(0, (j % 2 == 0 ? -12 : 9), 8);
            }
            Ball(dirty, "Tea stain", new Vector3(.25f, .304f, -.35f), new Vector3(.36f, .014f, .26f), Ink.Wood);
            Rounded(dirty, "Forgotten pillow", new Vector3(s * .83f, .15f, .35f), new Vector3(.55f, .24f, .55f), Ink.Linen).transform.localRotation = Quaternion.Euler(0, 24, 0);
            visual.dirty = dirty.gameObject;
            Transform upgraded = Group(dynamicRoom, "Bed upgrade / brass finials", bedAt);
            for (int z = -1; z <= 1; z += 2)
            {
                Cylinder(upgraded, "Upgrade headboard post", new Vector3(s * 1.23f, .22f, z * .77f), new Vector3(.08f, 1.27f, .08f), Ink.Brass);
                Ball(upgraded, "Upgrade finial", new Vector3(s * 1.23f, .9f, z * .77f), Vector3.one * .17f, Ink.Brass);
            }
            visual.upgraded = upgraded.gameObject;

            Vector3 sinkAt = HotelLayout.RoomTarget("sink", n);
            Transform sink = Group(p, "Washstand " + n, sinkAt);
            Rounded(sink, "Washstand cabinet", new Vector3(0, -.55f, 0), new Vector3(1.2f, .79f, .68f), Ink.Teal, true);
            Rounded(sink, "Ceramic basin rim", new Vector3(0, -.02f, 0), new Vector3(1.32f, .18f, .79f), Ink.Ceramic, true);
            Ball(sink, "Basin hollow", new Vector3(0, .07f, -.02f), new Vector3(.88f, .032f, .5f), Ink.Metal);
            Cylinder(sink, "Drain", new Vector3(0, .095f, -.01f), new Vector3(.095f, .011f, .095f), Ink.Dark);
            Tube(sink, "Tap stem", new Vector3(0, .05f, -.24f), new Vector3(0, .34f, -.24f), .075f, Ink.Brass);
            Tube(sink, "Tap spout", new Vector3(0, .34f, -.24f), new Vector3(0, .34f, .06f), .075f, Ink.Brass);
            Tube(sink, "Tap handle", new Vector3(-.17f, .2f, -.24f), new Vector3(.17f, .2f, -.24f), .035f, Ink.Brass);
            for (int j = -1; j <= 1; j += 2)
                Ball(sink, "Cabinet knob", new Vector3(j * .14f, -.43f, .353f), Vector3.one * .085f, Ink.Brass);
            Target(sink.gameObject, "sink_" + n, "Раковина — ремонт с ящиком инструментов");
            // A framed blue mirror and tiled splashback, on the actual south wall.
            Vector3 mirrorAt = new Vector3(sinkAt.x, 1.8f, c.z - 3.38f);
            Rounded(p, "Mirror brass frame", mirrorAt, new Vector3(1.25f, 1.24f, .09f), Ink.Brass);
            Rounded(p, "Mirror stylized blue reflection", mirrorAt + new Vector3(0, 0, .057f), new Vector3(1.08f, 1.07f, .035f), Ink.Blue);
            Tube(p, "Mirror glint", mirrorAt + new Vector3(-.33f, .16f, .081f), mirrorAt + new Vector3(.2f, .44f, .081f), .025f, Ink.Foam);
            Transform leak = Group(dynamicRoom, "Visible leak", sinkAt);
            Tube(leak, "Split drain pipe", new Vector3(.22f, -.22f, .35f), new Vector3(.32f, -.52f, .38f), .065f, Ink.Water);
            HotelLeak drip = leak.gameObject.AddComponent<HotelLeak>();
            drip.drops = new Transform[5];
            for (int j = 0; j < drip.drops.Length; ++j)
                drip.drops[j] = Ball(leak, "Falling droplet", new Vector3(.3f, -.2f - j * .13f, .41f), new Vector3(.045f, .085f, .045f), Ink.Water).transform;
            visual.leak = leak.gameObject;

            Transform water = Group(dynamicRoom, "Puddle " + n, HotelLayout.RoomTarget("water", n));
            for (int j = 0; j < 5; ++j)
            {
                float angle = j * 2.39996f;
                Ball(water, "Uneven water lobe", new Vector3(Mathf.Sin(angle) * .34f, 0, Mathf.Cos(angle) * .25f), new Vector3(1.29f, .045f, .94f), Ink.Water);
            }
            Tube(water, "Puddle reflection", new Vector3(-.43f, .026f, -.09f), new Vector3(.16f, .026f, -.21f), .025f, Ink.Foam);
            Tube(water, "Small reflection", new Vector3(.14f, .026f, .26f), new Vector3(.44f, .026f, .2f), .017f, Ink.Foam);
            BoxCollider waterCollider = water.gameObject.AddComponent<BoxCollider>();
            waterCollider.size = new Vector3(1.8f, .04f, 1.5f);
            Target(water.gameObject, "water_" + n, "Вода на полу — нужна швабра");
            visual.water = water;

            Transform stand = Group(p, "Towel stand", HotelLayout.RoomTarget("towel", n));
            for (int j = -1; j <= 1; j += 2)
            {
                Tube(stand, "Towel rail upright", new Vector3(j * .36f, -.97f, 0), new Vector3(j * .36f, .12f, 0), .045f, Ink.Brass);
                Box(stand, "Rail foot", new Vector3(j * .36f, -1.05f, 0), new Vector3(.18f, .08f, .45f), Ink.Brass, true);
            }
            Tube(stand, "Towel top rail", new Vector3(-.38f, .11f, 0), new Vector3(.38f, .11f, 0), .055f, Ink.Brass);
            // Small real collider makes the empty stand selectable without sealing off the washroom.
            BoxCollider railHit = stand.gameObject.AddComponent<BoxCollider>();
            railHit.center = new Vector3(0, .07f, 0); railHit.size = new Vector3(.85f, .12f, .15f);
            Target(stand.gameObject, "towel_" + n, "Стойка для чистого полотенца");
            Transform towel = Group(dynamicRoom, "Delivered towel", HotelLayout.RoomTarget("towel", n));
            Rounded(towel, "Hanging folded towel", new Vector3(0, -.17f, .03f), new Vector3(.52f, .56f, .1f), Ink.White);
            Box(towel, "Towel woven stripe", new Vector3(0, -.36f, .089f), new Vector3(.49f, .047f, .016f), Ink.Teal);
            visual.towel = towel.gameObject;

            Transform trash = Group(p, "Room bin", HotelLayout.RoomTarget("trash", n));
            Cylinder(trash, "Wastebasket", new Vector3(0, -.025f, 0), new Vector3(.44f, .51f, .44f), Ink.Wood, true);
            Cylinder(trash, "Wastebasket opening", new Vector3(0, .237f, 0), new Vector3(.37f, .018f, .37f), Ink.Dark);
            Target(trash.gameObject, "trash_" + n, "Мусор — собрать в мешок");
            Transform rubbish = Group(dynamicRoom, "Visible room rubbish", HotelLayout.RoomTarget("trash", n));
            for (int j = 0; j < 5; ++j)
                Ball(rubbish, "Crumpled paper", new Vector3(Mathf.Sin(j * 2f) * .14f, .25f + j * .023f, Mathf.Cos(j * 2f) * .13f), Vector3.one * .18f, j % 2 == 0 ? Ink.Paper : Ink.Linen);
            Rounded(rubbish, "Discarded carton", new Vector3(.3f, -.22f, .18f), new Vector3(.17f, .16f, .27f), Ink.Red).transform.localRotation = Quaternion.Euler(0, 32, 18);
            visual.trash = rubbish.gameObject;

            Transform luggage = Group(p, "Luggage stand", HotelLayout.RoomTarget("bag", n));
            Rounded(luggage, "Luggage stand top", new Vector3(0, -.05f, 0), new Vector3(.92f, .095f, .69f), Ink.Wood, true);
            for (int j = -1; j <= 1; j += 2)
            {
                Tube(luggage, "Folding luggage leg", new Vector3(-.4f, -.42f, j * .25f), new Vector3(.37f, -.1f, j * .25f), .055f, Ink.Metal);
                Tube(luggage, "Folding luggage leg", new Vector3(.4f, -.42f, j * .25f), new Vector3(-.37f, -.1f, j * .25f), .055f, Ink.Metal);
            }
            Text(luggage, "БАГАЖ", new Vector3(0, -.03f, -.365f), .065f, Ink.Cream);
            Target(luggage.gameObject, "bag_" + n, "Поставить багаж гостя");

            Toilet(p, c + new Vector3(-s * 1.65f, 0, -2.7f));
            Rounded(p, "Bedside cabinet", c + new Vector3(s * 2.05f, .37f, 2.46f), new Vector3(1.1f, .74f, .76f), Ink.Wood, true);
            Lamp(p, c + new Vector3(s * 2.05f, 1.27f, 2.46f), .34f);
            Cup(p, c + new Vector3(s * 1.8f, .78f, 2.46f));
            RoomWindow(p, c + new Vector3(s * 3.075f, 2.04f, .1f), s);
            Frame(p, c + new Vector3(s * .1f, 2.22f, 3.375f), 0, n % 2 == 0 ? "ВЫ ПРЕКРАСНО\nВЫГЛЯДИТЕ.\nОТЕЛЬ СТАРАЕТСЯ." : "МОРЕ ДАЛЕКО.\nЗАТО КРОВАТЬ\nБЛИЗКО.", 1.4f, 1.06f);
            // A tiny television on the inner wall leaves the crossing route unobstructed.
            Transform tv = Group(p, "Old television", c + new Vector3(-s * 2.96f, 1.9f, 2.26f));
            tv.localRotation = Quaternion.Euler(0, -s * 90, 0);
            Rounded(tv, "TV casing", Vector3.zero, new Vector3(1.13f, .73f, .2f), Ink.Dark);
            Rounded(tv, "TV sleepy screen", new Vector3(-.055f, .02f, -.12f), new Vector3(.86f, .52f, .03f), Ink.Teal);
            Text(tv, "НЕТ СИГНАЛА", new Vector3(-.055f, .02f, -.144f), .063f, Ink.Foam);

            Vector3 doorAt = HotelLayout.Door(n);
            Transform door = Group(p, "Always open room portal " + n, doorAt);
            for (int j = -1; j <= 1; j += 2)
                Box(door, "Portal jamb outside clear opening", new Vector3(0, 1.31f, j * .805f), new Vector3(.32f, 2.62f, .11f), Ink.Wood, true);
            Box(door, "Portal crown", new Vector3(0, 2.66f, 0), new Vector3(.35f, .15f, 1.74f), Ink.Oak, true);
            // Leaf is parked fully inside the room, parallel to the walking axis, never across the opening.
            Rounded(door, "Door leaf fixed open", new Vector3(s * .84f, 1.23f, .88f), new Vector3(1.47f, 2.46f, .12f), Ink.Wood, true);
            Box(door, "Open leaf inset", new Vector3(s * .84f, 1.31f, .801f), new Vector3(1.15f, 1.83f, .035f), Ink.Oak);
            Ball(door, "Door handle", new Vector3(s * 1.39f, 1.06f, .765f), Vector3.one * .085f, Ink.Brass);
            Rounded(door, "Room number plaque", new Vector3(-s * .165f, 1.74f, 1.04f), new Vector3(.04f, .49f, .61f), Ink.Burgundy, true);
            Text(door, n.ToString(), new Vector3(-s * .19f, 1.77f, 1.04f), .205f, Ink.Cream, s * 90);
            Target(door.gameObject, "door_" + n, "Номер " + n + " — доступность");
            visual.status = Text(dynamicRoom, "ГОТОВ", doorAt + new Vector3(-s * .197f, 1.4f, 1.04f), .064f, Ink.Teal, s * 90);
            visual.lamp = Ball(dynamicRoom, "Room status lamp", doorAt + new Vector3(-s * .191f, 2.09f, 1.04f), new Vector3(.075f, .1f, .1f), Ink.Green).GetComponent<Renderer>();
            Lamp(p, c + new Vector3(0, 3.15f, -.3f), .57f);
            SetActive(visual.dirty, false); SetActive(visual.leak, false); SetActive(visual.trash, false);
            SetActive(visual.upgraded, false); SetActive(visual.water.gameObject, false);
        }

        void ApplyRoom(RoomState room, RoomVisual v)
        {
            if (v.bed != room.bed)
            {
                SetActive(v.clean, room.bed == 2); SetActive(v.dirty, room.bed == 1); v.bed = room.bed;
            }
            SetActive(v.towel, room.towel); SetActive(v.trash, room.trash);
            SetActive(v.leak, room.leak); SetActive(v.upgraded, room.upgraded);
            float water = Mathf.Clamp01(room.water);
            if (Mathf.Abs(water - v.waterAmount) > .001f)
            {
                SetActive(v.water.gameObject, water > .001f);
                float size = Mathf.Lerp(.36f, 1.4f, Mathf.Sqrt(water));
                v.water.localScale = new Vector3(size, 1, size);
                v.waterAmount = water;
            }
            bool dirty = room.bed != 2 || room.trash || !room.towel || room.water > .001f;
            int flag = room.outOfService ? 4 : room.leak ? 3 : room.guestId != 0 ? 2 : dirty ? 1 : 0;
            if (flag != v.flag)
            {
                v.status.text = flag == 4 ? "ЗАКРЫТ" : flag == 3 ? "ТЕЧЬ!" : flag == 2 ? "ЗАНЯТ" : flag == 1 ? "УБОРКА" : "ГОТОВ";
                Ink color = flag == 4 || flag == 3 ? Ink.Red : flag == 1 ? Ink.Gold : flag == 2 ? Ink.Blue : Ink.Green;
                v.lamp.sharedMaterial = Mat(color); v.status.color = ColorOf(color); v.flag = flag;
            }
        }

        void ApplyStock(HotelState state)
        {
            if (state.linenStock != previousLinen)
            {
                linenCount.text = "ОСТАЛОСЬ: " + state.linenStock;
                for (int i = 0; i < linenPiles.Count; ++i) SetActive(linenPiles[i], i < Mathf.CeilToInt(state.linenStock * .5f));
                previousLinen = state.linenStock;
            }
            if (state.towelStock != previousTowels)
            {
                towelCount.text = "ОСТАЛОСЬ: " + state.towelStock;
                for (int i = 0; i < towelPiles.Count; ++i) SetActive(towelPiles[i], i < Mathf.CeilToInt(state.towelStock * .5f));
                previousTowels = state.towelStock;
            }
            if (previousPhase != state.phase)
            {
                openSign.text = state.phase == "open" ? "ДОБРО ПОЖАЛОВАТЬ!" : state.phase == "summary" ? "СМЕНА ЗАКРЫТА" : state.phase == "closing" ? "ЗАВЕРШАЕМ СМЕНУ" : "ГОТОВИМСЯ К СМЕНЕ";
                previousPhase = state.phase;
            }
        }

        /// <summary>Item pivot is at the bottom; all meshes and materials are shared and original.</summary>
        public static GameObject MakeItem(string kind)
        {
            EnsureAssets();
            Transform p = Group(null, "Item / " + kind);
            switch (kind)
            {
                case "toolbox":
                    Rounded(p, "Steel toolbox", new Vector3(0, .19f, 0), new Vector3(.64f, .34f, .35f), Ink.Red);
                    Rounded(p, "Toolbox lid", new Vector3(0, .36f, 0), new Vector3(.68f, .12f, .38f), Ink.Burgundy);
                    Handle(p, new Vector3(0, .43f, 0), .29f, .12f, Ink.Metal);
                    for (int j = -1; j <= 1; j += 2)
                        Box(p, "Toolbox latch", new Vector3(j * .19f, .31f, -.196f), new Vector3(.063f, .12f, .027f), Ink.Brass);
                    Box(p, "Toolbox badge", new Vector3(0, .17f, -.184f), new Vector3(.23f, .105f, .017f), Ink.Paper);
                    Text(p, "РЕМОНТ", new Vector3(0, .17f, -.196f), .045f, Ink.Dark);
                    break;
                case "mop":
                    Rounded(p, "Mop head", new Vector3(0, .055f, 0), new Vector3(.55f, .1f, .24f), Ink.Teal);
                    for (int j = 0; j < 11; ++j)
                        Tube(p, "Cotton mop strand", new Vector3(-.25f + j * .05f, .055f, -.12f), new Vector3(-.24f + j * .049f, .027f, .18f + (j % 3) * .025f), .034f, Ink.Linen);
                    Tube(p, "Wood mop pole", new Vector3(0, .1f, 0), new Vector3(.11f, 1.49f, .02f), .039f, Ink.Oak);
                    Tube(p, "Mop rubber grip", new Vector3(.089f, 1.24f, .017f), new Vector3(.11f, 1.52f, .02f), .06f, Ink.Teal);
                    break;
                case "linen":
                case "towel":
                case "dirtylinen":
                    bool towel = kind == "towel", dirty = kind == "dirtylinen";
                    int layers = towel ? 2 : 3;
                    for (int j = 0; j < layers; ++j)
                    {
                        GameObject fold = Rounded(p, "Folded textile", new Vector3(j % 2 * .012f, .055f + j * .081f, 0), new Vector3(towel ? .43f : .65f, .104f, towel ? .33f : .43f), dirty ? Ink.Dirty : Ink.White);
                        if (dirty) fold.transform.localRotation = Quaternion.Euler(0, j * 9 - 8, j * 3);
                        Box(p, "Woven edge", new Vector3(0, .06f + j * .081f, towel ? -.17f : -.22f), new Vector3(towel ? .38f : .6f, .02f, .011f), dirty ? Ink.Wood : Ink.Teal);
                    }
                    if (dirty) Ball(p, "Laundry stain", new Vector3(.12f, .28f, -.03f), new Vector3(.2f, .02f, .13f), Ink.Wood);
                    else Box(p, "Laundry paper band", new Vector3(0, layers * .081f + .036f, 0), new Vector3(.1f, .012f, towel ? .32f : .44f), Ink.Paper);
                    break;
                case "trashbag":
                    Ball(p, "Lopsided rubbish bag", new Vector3(0, .25f, 0), new Vector3(.56f, .5f, .5f), Ink.Dark);
                    Ball(p, "Bag bulge", new Vector3(.16f, .2f, -.08f), new Vector3(.31f, .31f, .34f), Ink.Dark);
                    Shape(p, "Gathered bag neck", cone, new Vector3(0, .51f, 0), new Vector3(.2f, .16f, .2f), Ink.Dark);
                    Tube(p, "Bag tie", new Vector3(-.12f, .49f, 0), new Vector3(.12f, .52f, 0), .034f, Ink.Gold);
                    break;
                case "cart":
                    Rounded(p, "Luggage cart platform", new Vector3(0, .22f, 0), new Vector3(1.04f, .13f, .68f), Ink.Wood);
                    Box(p, "Cart carpet", new Vector3(0, .295f, 0), new Vector3(.94f, .025f, .59f), Ink.Burgundy);
                    for (int x = -1; x <= 1; x += 2)
                        for (int z = -1; z <= 1; z += 2)
                        {
                            GameObject wheel = Cylinder(p, "Cart wheel", new Vector3(x * .42f, .1f, z * .26f), new Vector3(.17f, .08f, .17f), Ink.Dark);
                            wheel.transform.localRotation = Quaternion.Euler(0, 0, 90);
                            Tube(p, "Cart brass upright", new Vector3(x * .47f, .29f, z * .28f), new Vector3(x * .47f, 1.23f, z * .28f), .045f, Ink.Brass);
                        }
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Tube(p, "Cart upper arch", new Vector3(-.47f, 1.23f, z * .28f), new Vector3(-.28f, 1.43f, z * .28f), .047f, Ink.Brass);
                        Tube(p, "Cart upper arch", new Vector3(.47f, 1.23f, z * .28f), new Vector3(.28f, 1.43f, z * .28f), .047f, Ink.Brass);
                        Tube(p, "Cart top bar", new Vector3(-.28f, 1.43f, z * .28f), new Vector3(.28f, 1.43f, z * .28f), .047f, Ink.Brass);
                    }
                    Handle(p, new Vector3(0, .91f, -.35f), .56f, .13f, Ink.Brass);
                    break;
                default: // "bag"; a recognizable hard-shell, wheel-mounted suitcase.
                    Rounded(p, "Suitcase shell", new Vector3(0, .37f, 0), new Vector3(.55f, .63f, .3f), Ink.Teal);
                    Rounded(p, "Suitcase raised front", new Vector3(0, .38f, -.16f), new Vector3(.45f, .47f, .06f), Ink.Blue);
                    for (int j = -1; j <= 1; ++j)
                        Box(p, "Suitcase rib", new Vector3(j * .13f, .38f, -.2f), new Vector3(.018f, .36f, .02f), Ink.Teal);
                    for (int j = -1; j <= 1; j += 2)
                    {
                        Box(p, "Leather luggage strap", new Vector3(j * .19f, .37f, -.204f), new Vector3(.041f, .55f, .014f), Ink.Brass);
                        Ball(p, "Suitcase wheel", new Vector3(j * .2f, .055f, .03f), new Vector3(.09f, .11f, .09f), Ink.Dark);
                    }
                    Handle(p, new Vector3(0, .7f, 0), .25f, .13f, Ink.Dark);
                    Rounded(p, "Luggage tag", new Vector3(.13f, .6f, -.23f), new Vector3(.16f, .19f, .027f), Ink.Paper).transform.localRotation = Quaternion.Euler(0, 0, -13);
                    Text(p, "ГРАНД", new Vector3(.13f, .61f, -.249f), .031f, Ink.Dark);
                    break;
            }
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            foreach (Renderer renderer in p.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(renderer.bounds);
            BoxCollider hit = p.gameObject.AddComponent<BoxCollider>();
            hit.center = bounds.center; hit.size = Vector3.Max(bounds.size, Vector3.one * .08f);
            Target(p.gameObject, kind ?? "bag", ItemLabel(kind));
            return p.gameObject;
        }

        /// <summary>Seeded, recognizable humanoid with pivot at its feet, facing local +Z.</summary>
        public static GameObject MakePerson(bool employee, int seed)
        {
            EnsureAssets();
            Transform p = Group(null, employee ? "Hotel employee" : "Hotel guest");
            HotelFigure rig = p.gameObject.AddComponent<HotelFigure>();
            int variation = (seed & int.MaxValue) % 4;
            Ink skin = variation == 2 ? Ink.SkinDark : Ink.Skin;
            Ink outfit = employee ? Ink.Burgundy : variation == 0 ? Ink.Teal : variation == 1 ? Ink.Oak : variation == 2 ? Ink.Blue : Ink.Green;
            rig.body = Group(p, "Animated silhouette");
            Rounded(rig.body, "Tailored torso", new Vector3(0, 1.035f, 0), new Vector3(.49f, .59f, .29f), outfit);
            Rounded(rig.body, "Trousers hips", new Vector3(0, .723f, 0), new Vector3(.37f, .24f, .27f), Ink.Dark);
            for (int side = -1; side <= 1; side += 2)
            {
                Transform leg = Group(rig.body, side < 0 ? "Left leg pivot" : "Right leg pivot", new Vector3(side * .115f, .71f, 0));
                Rounded(leg, "Trouser leg", new Vector3(0, -.265f, 0), new Vector3(.145f, .5f, .19f), Ink.Dark);
                Rounded(leg, "Oversized shoe", new Vector3(0, -.615f, .065f), new Vector3(.185f, .16f, .33f), Ink.Wood);
                if (side < 0) rig.leftLeg = leg; else rig.rightLeg = leg;
                Transform arm = Group(rig.body, side < 0 ? "Left shoulder" : "Right shoulder", new Vector3(side * .292f, 1.24f, 0));
                Rounded(arm, "Jacket sleeve", new Vector3(side * .025f, -.21f, 0), new Vector3(.15f, .43f, .18f), outfit);
                Rounded(arm, "Shirt cuff", new Vector3(side * .025f, -.411f, 0), new Vector3(.153f, .063f, .183f), Ink.White);
                Ball(arm, "Mitten hand", new Vector3(side * .025f, -.48f, .015f), new Vector3(.155f, .17f, .15f), skin);
                if (side < 0) rig.leftArm = arm; else rig.rightArm = arm;
            }
            rig.head = Group(rig.body, "Expressive head", new Vector3(0, 1.48f, 0));
            Cylinder(rig.head, "Neck", new Vector3(0, -.095f, 0), new Vector3(.16f, .22f, .16f), skin);
            Ball(rig.head, "Faceted head", new Vector3(0, .1f, .025f), new Vector3(.41f, .47f, .36f), skin);
            Ball(rig.head, "Comical nose", new Vector3(0, .063f, .206f), new Vector3(.1f, .13f, .14f), skin);
            for (int side = -1; side <= 1; side += 2)
            {
                Ball(rig.head, "Ear", new Vector3(side * .208f, .071f, .019f), new Vector3(.07f, .13f, .08f), skin);
                Ball(rig.head, "Eye white", new Vector3(side * .089f, .155f, .18f), new Vector3(.089f, .082f, .033f), Ink.White);
                Ball(rig.head, "Dark pupil", new Vector3(side * .08f, .153f, .202f), new Vector3(.037f, .047f, .015f), Ink.Dark);
                Rounded(rig.head, "Raised eyebrow", new Vector3(side * .089f, .224f, .179f), new Vector3(.104f, .028f, .04f), Ink.Hair).transform.localRotation = Quaternion.Euler(0, 0, side * (variation % 2 == 0 ? -8 : 11));
            }
            rig.mouth = Rounded(rig.head, "Wry smile", new Vector3(0, -.028f, .174f), new Vector3(.107f, .022f, .025f), Ink.Hair).transform;
            Ball(rig.head, "Hair cap", new Vector3(0, .286f, -.014f), new Vector3(.407f, .18f, .344f), Ink.Hair);
            Rounded(rig.head, "Unruly fringe", new Vector3(.065f, .249f, .145f), new Vector3(.21f, .1f, .09f), Ink.Hair).transform.localRotation = Quaternion.Euler(0, 0, 13);
            Box(rig.body, "Shirt collar", new Vector3(0, 1.249f, .159f), new Vector3(.22f, .105f, .028f), Ink.White);
            if (employee)
            {
                for (int j = 0; j < 3; ++j)
                    Ball(rig.body, "Uniform brass button", new Vector3(0, 1.13f - j * .13f, .165f), Vector3.one * .036f, Ink.Brass);
                Box(rig.body, "Employee badge", new Vector3(-.139f, 1.126f, .162f), new Vector3(.11f, .06f, .027f), Ink.Brass);
                Cylinder(rig.head, "Bellhop pillbox hat", new Vector3(0, .411f, 0), new Vector3(.36f, .18f, .32f), Ink.Burgundy);
                Cylinder(rig.head, "Hat gold piping", new Vector3(0, .477f, 0), new Vector3(.366f, .023f, .326f), Ink.Brass);
                Ball(rig.head, "Hat badge", new Vector3(0, .407f, .164f), new Vector3(.07f, .085f, .022f), Ink.Brass);
            }
            else if (variation == 0)
            {
                Box(rig.body, "Business tie", new Vector3(0, 1.103f, .174f), new Vector3(.067f, .27f, .023f), Ink.Burgundy);
                for (int side = -1; side <= 1; side += 2)
                    Rounded(rig.head, "Glasses rim", new Vector3(side * .09f, .152f, .225f), new Vector3(.135f, .1f, .02f), Ink.Brass);
            }
            else if (variation == 1)
            {
                Box(rig.body, "Tourist camera strap", new Vector3(0, 1.12f, .168f), new Vector3(.035f, .28f, .027f), Ink.Dark);
                Rounded(rig.body, "Tourist pocket camera", new Vector3(0, 1.01f, .218f), new Vector3(.22f, .14f, .1f), Ink.Dark);
                Ball(rig.body, "Camera lens", new Vector3(0, 1.01f, .277f), new Vector3(.095f, .095f, .04f), Ink.Metal);
            }
            else if (variation == 2)
            {
                Cylinder(rig.head, "Sun hat brim", new Vector3(0, .348f, 0), new Vector3(.6f, .04f, .54f), Ink.Linen);
                Shape(rig.head, "Sun hat crown", cone, new Vector3(0, .433f, 0), new Vector3(.38f, .19f, .33f), Ink.Oak);
            }
            else
                Rounded(rig.body, "Bright scarf", new Vector3(.08f, 1.235f, .2f), new Vector3(.1f, .3f, .1f), Ink.Gold);
            CapsuleCollider collider = p.gameObject.AddComponent<CapsuleCollider>();
            collider.center = new Vector3(0, .88f, 0); collider.height = 1.76f; collider.radius = .275f;
            rig.phase = variation * 1.37f;
            return p.gameObject;
        }

        PersonVisual CreatePerson(bool employee, int seed, string id, string label)
        {
            GameObject root = MakePerson(employee, seed);
            root.name = id; root.transform.SetParent(living, false);
            Target(root, id, label);
            Transform sign = Group(root.transform, "Readable in-world name", new Vector3(0, 2.13f, 0));
            Rounded(sign, "Name card backing", Vector3.zero, new Vector3(.94f, .3f, .027f), Ink.Paper);
            TextMesh caption = Text(sign, employee ? "СОТРУДНИК" : "ГОСТЬ", new Vector3(0, 0, -.021f), .068f, Ink.Dark);
            HotelFigure figure = root.GetComponent<HotelFigure>(); figure.caption = sign;
            return new PersonVisual { root = root, figure = figure, caption = caption };
        }

        static string GuestCaption(GuestState guest)
        {
            string name = string.IsNullOrEmpty(guest.name) ? "Гость " + guest.id : guest.name;
            if (name.Length > 17) name = name.Substring(0, 16) + "…";
            string line = guest.stage == "queue" ? "ЖДЁТ НОМЕР" : guest.stage == "checkout" ? "ВЫЕЗД" :
                guest.stage == "leaving" ? "ДО СВИДАНИЯ" : guest.towelRequested ? "НУЖНО ПОЛОТЕНЦЕ" :
                !guest.luggageDelivered ? "ЖДЁТ БАГАЖ" : guest.satisfaction < 45 ? "НЕДОВОЛЕН" : "НОМЕР " + guest.room;
            return name + "\n" + line;
        }

        static string ItemLabel(string kind)
        {
            switch (kind)
            {
                case "toolbox": return "Ящик инструментов";
                case "mop": return "Швабра";
                case "linen": return "Чистое постельное бельё";
                case "dirtylinen": return "Грязное бельё → корзина";
                case "towel": return "Чистое полотенце";
                case "trashbag": return "Мешок мусора → контейнер";
                case "cart": return "Багажная тележка";
                default: return "Чемодан гостя";
            }
        }

        void BuildLighting()
        {
            Transform p = Group(hotelRoot, "Warm practical lighting");
            Light sun = Group(p, "Late afternoon sun").gameObject.AddComponent<Light>();
            sun.type = LightType.Directional; sun.color = new Color(1, .86f, .68f);
            sun.intensity = 1.1f; sun.shadows = LightShadows.Soft; sun.shadowStrength = .72f;
            sun.transform.localRotation = Quaternion.Euler(52, -32, 0);
            PointLight(p, new Vector3(0, 2.85f, -2.8f), 7.2f, 4.5f, true);
            PointLight(p, new Vector3(5, 2.8f, -3.5f), 5, 2.1f, false);
            PointLight(p, new Vector3(-4.8f, 2.7f, -2.2f), 4.5f, 2.6f, false);
            for (int n = 101; n <= 104; ++n)
                PointLight(p, HotelLayout.RoomCenter(n) + new Vector3(0, 2.8f, -.2f), 5.9f, 3.2f, true);
            foreach (float z in new[] { 3.1f, 8.6f, 14.3f })
            {
                Lamp(architecture, new Vector3(0, 3.12f, z), .36f);
                PointLight(p, new Vector3(0, 2.8f, z), 4.7f, 2.4f, false);
            }
        }

        static void PointLight(Transform parent, Vector3 position, float range, float intensity, bool shadows)
        {
            Light light = Group(parent, "Warm pool of light", position).gameObject.AddComponent<Light>();
            light.type = LightType.Point; light.range = range; light.intensity = intensity;
            light.color = new Color(1, .86f, .69f); light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            light.shadowStrength = .7f; light.shadowBias = .045f; light.shadowNormalBias = .22f;
            light.shadowCustomResolution = 512;
        }

        static void Wall(Transform p, Vector3 basePosition, Vector3 size)
        {
            Box(p, "Cream plaster wall", basePosition + Vector3.up * (size.y * .5f), size, Ink.Plaster, true);
            bool alongX = size.x > size.z;
            Vector3 lower = new Vector3(size.x + (alongX ? 0 : .028f), .98f, size.z + (alongX ? .028f : 0));
            Box(p, "Wooden wainscot", basePosition + Vector3.up * .49f, lower, Ink.Wood);
            Box(p, "Wainscot chair rail", basePosition + Vector3.up * 1.015f,
                new Vector3(lower.x + (alongX ? 0 : .035f), .085f, lower.z + (alongX ? .035f : 0)), Ink.Oak);
            Box(p, "Cream crown moulding", basePosition + Vector3.up * 3.43f,
                new Vector3(lower.x + (alongX ? 0 : .06f), .16f, lower.z + (alongX ? .06f : 0)), Ink.Cream);
            Box(p, "Dark skirting", basePosition + Vector3.up * .07f,
                new Vector3(lower.x + (alongX ? 0 : .02f), .14f, lower.z + (alongX ? .02f : 0)), Ink.Dark);
            float length = alongX ? size.x : size.z;
            for (float t = -length * .5f + .38f; t < length * .5f - .1f; t += .72f)
            {
                Vector3 at = basePosition + new Vector3(alongX ? t : 0, .51f, alongX ? 0 : t);
                Box(p, "Wainscot batten", at, new Vector3(alongX ? .035f : lower.x + .017f, .88f, alongX ? lower.z + .017f : .035f), Ink.Oak);
            }
            // Deliberate large plaster repairs, with no fine texture noise.
            if (length > 4)
            {
                Vector3 at = basePosition + new Vector3(alongX ? length * .31f : 0, 1.26f, alongX ? 0 : length * .31f);
                Box(p, "Old plaster patch", at, new Vector3(alongX ? .47f : size.x + .009f, .16f, alongX ? size.z + .009f : .47f), Ink.Cream);
            }
        }

        static void Rug(Transform p, Vector3 position, Vector2 size)
        {
            Box(p, "Burgundy rug", position, new Vector3(size.x, .025f, size.y), Ink.Burgundy);
            for (int s = -1; s <= 1; s += 2)
            {
                Box(p, "Rug side piping", position + new Vector3(s * (size.x * .5f - .085f), .015f, 0), new Vector3(.035f, .007f, size.y - .15f), Ink.Brass);
                Box(p, "Rug end piping", position + new Vector3(0, .015f, s * (size.y * .5f - .085f)), new Vector3(size.x - .15f, .007f, .035f), Ink.Brass);
            }
        }

        static void Sofa(Transform p, Vector3 position, float yaw)
        {
            Transform sofa = Group(p, "Lounge sofa", position); sofa.localRotation = Quaternion.Euler(0, yaw, 0);
            Rounded(sofa, "Sofa frame", new Vector3(0, .32f, 0), new Vector3(2.36f, .4f, .81f), Ink.Wood, true);
            Rounded(sofa, "Sofa back", new Vector3(0, .76f, .31f), new Vector3(2.29f, .81f, .23f), Ink.Burgundy, true);
            for (int j = -1; j <= 1; ++j)
            {
                Rounded(sofa, "Seat cushion", new Vector3(j * .66f, .54f, -.06f), new Vector3(.63f, .22f, .7f), Ink.Burgundy);
                Ball(sofa, "Back button", new Vector3(j * .66f, .82f, .179f), new Vector3(.05f, .05f, .018f), Ink.Brass);
            }
            for (int j = -1; j <= 1; j += 2)
            {
                Rounded(sofa, "Rolled armrest", new Vector3(j * 1.08f, .64f, 0), new Vector3(.28f, .39f, .91f), Ink.Burgundy, true);
                Rounded(sofa, "Splayed sofa foot", new Vector3(j * .9f, .11f, 0), new Vector3(.16f, .22f, .55f), Ink.Wood);
            }
            Rounded(sofa, "Faded throw pillow", new Vector3(.61f, .82f, .02f), new Vector3(.44f, .44f, .16f), Ink.Gold).transform.localRotation = Quaternion.Euler(10, 0, 17);
        }

        static void Lamp(Transform p, Vector3 position, float width)
        {
            Transform lamp = Group(p, "Pleated warm lampshade", position);
            Shape(lamp, "Tapered lampshade", cone, Vector3.zero, new Vector3(width, width * .62f, width), Ink.Linen);
            Cylinder(lamp, "Shade lower piping", new Vector3(0, -width * .31f, 0), new Vector3(width * 1.01f, .025f, width * 1.01f), Ink.Burgundy);
            Ball(lamp, "Warm bulb", new Vector3(0, -width * .2f, 0), Vector3.one * width * .22f, Ink.Glow);
            if (position.y < 2)
            {
                Tube(lamp, "Bedside lamp stem", new Vector3(0, -width * .32f, 0), new Vector3(0, -.45f, 0), .035f, Ink.Brass);
                Cylinder(lamp, "Lamp foot", new Vector3(0, -.48f, 0), new Vector3(.29f, .05f, .29f), Ink.Brass);
            }
            else Tube(lamp, "Pendant cord", new Vector3(0, width * .3f, 0), new Vector3(0, 3.55f - position.y, 0), .024f, Ink.Dark);
        }

        static void Bell(Transform p, Vector3 position)
        {
            Cylinder(p, "Desk bell base", position, new Vector3(.25f, .045f, .25f), Ink.Dark);
            Ball(p, "Desk bell dome", position + Vector3.up * .05f, new Vector3(.22f, .12f, .22f), Ink.Brass);
            Cylinder(p, "Bell button", position + Vector3.up * .14f, new Vector3(.07f, .05f, .07f), Ink.Brass);
        }

        static void Cup(Transform p, Vector3 position)
        {
            Cylinder(p, "Coffee cup", position + Vector3.up * .075f, new Vector3(.12f, .15f, .12f), Ink.Ceramic);
            Cylinder(p, "Cold coffee", position + Vector3.up * .15f, new Vector3(.09f, .007f, .09f), Ink.Wood);
            Handle(p, position + new Vector3(.085f, .05f, 0), .05f, .07f, Ink.Ceramic);
        }

        static void Handle(Transform p, Vector3 at, float width, float height, Ink ink)
        {
            Tube(p, "Handle left", at + Vector3.left * width * .5f, at + new Vector3(-width * .5f, height, 0), .034f, ink);
            Tube(p, "Handle right", at + Vector3.right * width * .5f, at + new Vector3(width * .5f, height, 0), .034f, ink);
            Tube(p, "Handle top", at + new Vector3(-width * .5f, height, 0), at + new Vector3(width * .5f, height, 0), .043f, ink);
        }

        static void Plant(Transform p, Vector3 at, float height)
        {
            Shape(p, "Terracotta plant pot", cone, at + Vector3.up * .23f, new Vector3(.5f, .46f, .5f), Ink.Red).transform.localRotation = Quaternion.Euler(180, 0, 0);
            Cylinder(p, "Plant soil", at + Vector3.up * .46f, new Vector3(.46f, .025f, .46f), Ink.Wood);
            for (int j = 0; j < 7; ++j)
            {
                float a = j * 2.39996f;
                Vector3 tip = at + new Vector3(Mathf.Sin(a) * .33f, height * (.72f + (j % 3) * .13f), Mathf.Cos(a) * .33f);
                Tube(p, "Plant stem", at + Vector3.up * .4f, tip, .02f, Ink.Green);
                GameObject leaf = Ball(p, "Broad stylized leaf", tip, new Vector3(.2f, .43f, .075f), j % 2 == 0 ? Ink.Leaf : Ink.Green);
                leaf.transform.localRotation = Quaternion.Euler(24, a * Mathf.Rad2Deg, 35);
            }
        }

        static void Frame(Transform p, Vector3 at, float yaw, string caption, float width, float height)
        {
            Transform frame = Group(p, "Framed hotel motto", at); frame.localRotation = Quaternion.Euler(0, yaw, 0);
            Rounded(frame, "Frame", Vector3.zero, new Vector3(width, height, .065f), Ink.Wood);
            Box(frame, "Print paper", new Vector3(0, 0, -.042f), new Vector3(width - .12f, height - .12f, .014f), Ink.Linen);
            Text(frame, caption, new Vector3(0, 0, -.052f), width > 1 ? .094f : .063f, Ink.Burgundy);
            Box(frame, "Print top rule", new Vector3(0, height * .32f, -.054f), new Vector3(width * .55f, .016f, .009f), Ink.Brass);
        }

        static void Clock(Transform p, Vector3 at)
        {
            GameObject outer = Cylinder(p, "Reception clock rim", at, new Vector3(.62f, .055f, .62f), Ink.Brass);
            outer.transform.localRotation = Quaternion.Euler(90, 0, 0);
            GameObject face = Cylinder(p, "Clock enamel dial", at + Vector3.back * .04f, new Vector3(.53f, .02f, .53f), Ink.Cream);
            face.transform.localRotation = Quaternion.Euler(90, 0, 0);
            Tube(p, "Clock minute hand", at + Vector3.back * .064f, at + new Vector3(.12f, .17f, -.064f), .019f, Ink.Dark);
            Tube(p, "Clock hour hand", at + Vector3.back * .067f, at + new Vector3(-.12f, .01f, -.067f), .024f, Ink.Dark);
            for (int j = 0; j < 12; ++j)
            {
                float a = j * Mathf.PI / 6;
                Ball(p, "Clock hour mark", at + new Vector3(Mathf.Sin(a) * .23f, Mathf.Cos(a) * .23f, -.059f), new Vector3(.025f, .025f, .012f), Ink.Wood);
            }
        }

        static void Toilet(Transform p, Vector3 at)
        {
            Transform toilet = Group(p, "Compact washroom toilet", at);
            Ball(toilet, "Toilet pedestal", new Vector3(0, .22f, .03f), new Vector3(.36f, .43f, .47f), Ink.Ceramic);
            Ball(toilet, "Toilet bowl", new Vector3(0, .4f, .13f), new Vector3(.48f, .22f, .65f), Ink.Ceramic);
            Ball(toilet, "Toilet seat", new Vector3(0, .51f, .16f), new Vector3(.49f, .07f, .61f), Ink.White);
            Ball(toilet, "Toilet inner bowl", new Vector3(0, .547f, .16f), new Vector3(.31f, .015f, .41f), Ink.Metal);
            Rounded(toilet, "Cistern", new Vector3(0, .73f, -.21f), new Vector3(.5f, .58f, .22f), Ink.Ceramic);
            Box(toilet, "Cistern lid", new Vector3(0, 1.034f, -.21f), new Vector3(.55f, .05f, .26f), Ink.White);
            Ball(toilet, "Flush button", new Vector3(.16f, .83f, -.083f), Vector3.one * .055f, Ink.Brass);
            BoxCollider collider = toilet.gameObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(0, .5f, .04f); collider.size = new Vector3(.52f, 1, .78f);
        }

        static void RoomWindow(Transform p, Vector3 at, float sign)
        {
            Transform window = Group(p, "Scenic window", at); window.localRotation = Quaternion.Euler(0, sign * 90, 0);
            Box(window, "Window casing", Vector3.zero, new Vector3(1.96f, 1.73f, .14f), Ink.Wood);
            Box(window, "Powder-blue daylight", new Vector3(0, 0, -.081f), new Vector3(1.78f, 1.55f, .03f), Ink.Blue);
            Ball(window, "Distant warm sun", new Vector3(.44f, .37f, -.106f), new Vector3(.29f, .29f, .021f), Ink.Glow);
            for (int j = 0; j < 4; ++j)
                Ball(window, "Distant stylized hill", new Vector3(-.69f + j * .48f, -.53f, -.119f), new Vector3(.77f, .5f + (j % 2) * .21f, .025f), j % 2 == 0 ? Ink.Teal : Ink.Green);
            Box(window, "Window mullion", new Vector3(0, 0, -.144f), new Vector3(.065f, 1.63f, .05f), Ink.Cream);
            Box(window, "Window crossbar", new Vector3(0, .1f, -.147f), new Vector3(1.83f, .07f, .05f), Ink.Cream);
            Box(window, "Window sill", new Vector3(0, -.87f, -.075f), new Vector3(2.03f, .13f, .29f), Ink.Cream);
            for (int side = -1; side <= 1; side += 2)
                for (int j = 0; j < 3; ++j)
                    Cylinder(window, "Burgundy curtain pleat", new Vector3(side * (.77f + j * .11f), -.035f, -.22f), new Vector3(.16f, 1.87f, .12f), Ink.Burgundy);
            Tube(window, "Curtain rod", new Vector3(-1.15f, .98f, -.19f), new Vector3(1.15f, .98f, -.19f), .04f, Ink.Brass);
        }

        static Transform Group(Transform parent, string name, Vector3 position = default)
        {
            Transform t = new GameObject(name).transform;
            if (parent != null) t.SetParent(parent, false);
            t.localPosition = position; return t;
        }

        static GameObject Shape(Transform parent, string name, Mesh mesh, Vector3 position, Vector3 size, Ink ink, bool solid = false)
        {
            Transform t = Group(parent, name, position); t.localScale = size;
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = t.gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial = Mat(ink);
            renderer.shadowCastingMode = ink == Ink.Glow || ink == Ink.Water || ink == Ink.Foam ? ShadowCastingMode.Off : ShadowCastingMode.On;
            renderer.receiveShadows = true;
            if (solid) t.gameObject.AddComponent<BoxCollider>();
            return t.gameObject;
        }
        static GameObject Box(Transform p, string name, Vector3 at, Vector3 size, Ink ink, bool solid = false) => Shape(p, name, cube, at, size, ink, solid);
        static GameObject Rounded(Transform p, string name, Vector3 at, Vector3 size, Ink ink, bool solid = false) => Shape(p, name, bevel, at, size, ink, solid);
        static GameObject Ball(Transform p, string name, Vector3 at, Vector3 size, Ink ink) => Shape(p, name, sphere, at, size, ink);
        static GameObject Cylinder(Transform p, string name, Vector3 at, Vector3 size, Ink ink, bool solid = false) => Shape(p, name, cylinder, at, size, ink, solid);

        static void Tube(Transform p, string name, Vector3 a, Vector3 b, float diameter, Ink ink)
        {
            Vector3 difference = b - a;
            GameObject tube = Cylinder(p, name, (a + b) * .5f, new Vector3(diameter, difference.magnitude, diameter), ink);
            if (difference.sqrMagnitude > .000001f) tube.transform.localRotation = Quaternion.FromToRotation(Vector3.up, difference);
        }

        static TextMesh Text(Transform p, string text, Vector3 at, float size, Ink ink, float yaw = 0)
        {
            Transform t = Group(p, "Lettering / " + text.Replace('\n', ' '), at); t.localRotation = Quaternion.Euler(0, yaw, 0);
            TextMesh mesh = t.gameObject.AddComponent<TextMesh>(); mesh.font = font; mesh.fontSize = 64;
            // fontSize controls raster resolution; characterSize controls actual world size.
            mesh.characterSize = size * (10f / 64f); mesh.anchor = TextAnchor.MiddleCenter; mesh.alignment = TextAlignment.Center;
            mesh.lineSpacing = 1.02f; mesh.color = ColorOf(ink); mesh.text = text;
            MeshRenderer renderer = mesh.GetComponent<MeshRenderer>(); renderer.sharedMaterial = fontMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            return mesh;
        }

        static void Target(GameObject root, string id, string label)
        {
            HotelTarget rootTarget = root.GetComponent<HotelTarget>();
            if (rootTarget == null) rootTarget = root.AddComponent<HotelTarget>();
            rootTarget.id = id; rootTarget.label = label;
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                HotelTarget target = collider.GetComponent<HotelTarget>();
                if (target == null) target = collider.gameObject.AddComponent<HotelTarget>();
                target.id = id; target.label = label;
            }
        }
        static void SetTargetLabel(GameObject root, string label)
        {
            foreach (HotelTarget target in root.GetComponentsInChildren<HotelTarget>(true)) target.label = label;
        }
        static void SetActive(GameObject root, bool active) { if (root.activeSelf != active) root.SetActive(active); }
        static void Retire(GameObject root)
        {
            if (root == null) return;
            root.SetActive(false);
            if (Application.isPlaying) Destroy(root); else DestroyImmediate(root);
        }

        static void EnsureAssets()
        {
            if (cube == null) cube = MakeBevel(0);
            if (bevel == null) bevel = MakeBevel(.085f);
            if (cylinder == null) cylinder = Lathe(new[] { new Vector2(0, -.5f), new Vector2(.5f, -.5f), new Vector2(.5f, .5f), new Vector2(0, .5f) }, 12, "Twelve-sided cylinder");
            if (cone == null) cone = Lathe(new[] { new Vector2(0, -.5f), new Vector2(.5f, -.5f), new Vector2(.29f, .5f), new Vector2(0, .5f) }, 12, "Tapered shade");
            if (sphere == null)
            {
                Vector2[] profile = new Vector2[9];
                for (int j = 0; j < profile.Length; ++j)
                {
                    float a = -Mathf.PI * .5f + j * Mathf.PI / (profile.Length - 1);
                    profile[j] = new Vector2(Mathf.Cos(a) * .5f, Mathf.Sin(a) * .5f);
                }
                sphere = Lathe(profile, 12, "Original faceted ellipsoid");
            }
            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                Font.textureRebuilt -= RebuildFontAtlas; Font.textureRebuilt += RebuildFontAtlas;
            }
            if (fontMaterial == null)
            {
                Shader shader = Resources.Load<Shader>("Hotel/HotelLettering");
                if (shader == null) throw new InvalidOperationException("Missing Resources/Hotel/HotelLettering.shader");
                fontMaterial = new Material(shader) { name = "Hotel Cyrillic lettering / shared" };
                fontMaterial.mainTexture = font.material.mainTexture;
            }
        }
        static void RebuildFontAtlas(Font changed)
        {
            if (changed == font && fontMaterial != null) fontMaterial.mainTexture = changed.material.mainTexture;
        }
        static Material Mat(Ink ink)
        {
            if (Materials.TryGetValue(ink, out Material material) && material != null) return material;
            Shader shader = Resources.Load<Shader>("Hotel/HotelSurface");
            if (shader == null) throw new InvalidOperationException("Missing Resources/Hotel/HotelSurface.shader");
            material = new Material(shader) { name = "Hotel / " + ink, enableInstancing = true };
            material.SetColor("_BaseColor", ColorOf(ink));
            material.SetFloat("_Smoothness", ink == Ink.Water ? .85f : ink == Ink.Brass || ink == Ink.Ceramic ? .55f : .08f);
            material.SetFloat("_Emission", ink == Ink.Glow ? .8f : ink == Ink.Foam ? .23f : 0);
            Materials[ink] = material; return material;
        }
        static Color ColorOf(Ink ink)
        {
            switch (ink)
            {
                case Ink.Cream: return new Color(.94f, .84f, .65f);
                case Ink.Plaster: return new Color(.88f, .78f, .62f);
                case Ink.Wood: return new Color(.34f, .2f, .14f);
                case Ink.Oak: return new Color(.59f, .38f, .22f);
                case Ink.Burgundy: return new Color(.43f, .095f, .14f);
                case Ink.Red: return new Color(.72f, .23f, .16f);
                case Ink.Brass: return new Color(.77f, .56f, .25f);
                case Ink.White: return new Color(.98f, .96f, .86f);
                case Ink.Linen: return new Color(.83f, .78f, .62f);
                case Ink.Dirty: return new Color(.56f, .47f, .32f);
                case Ink.Dark: return new Color(.12f, .15f, .17f);
                case Ink.Metal: return new Color(.38f, .47f, .47f);
                case Ink.Ceramic: return new Color(.8f, .86f, .79f);
                case Ink.Water: return new Color(.19f, .66f, .78f);
                case Ink.Foam: return new Color(.73f, .95f, .98f);
                case Ink.Teal: return new Color(.18f, .4f, .38f);
                case Ink.Green: return new Color(.29f, .49f, .27f);
                case Ink.Leaf: return new Color(.49f, .65f, .3f);
                case Ink.Gold: return new Color(.94f, .65f, .22f);
                case Ink.Blue: return new Color(.43f, .64f, .69f);
                case Ink.Skin: return new Color(.86f, .61f, .4f);
                case Ink.SkinDark: return new Color(.47f, .29f, .19f);
                case Ink.Hair: return new Color(.19f, .12f, .1f);
                case Ink.Glow: return new Color(1, .85f, .55f);
                default: return new Color(.98f, .91f, .73f);
            }
        }

        // Meshes use independent face vertices for deliberate flat shading. Winding is verified
        // against an outward direction; there are no downloaded textures, models or prefabs.
        static Mesh MakeBevel(float inset)
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            float h = .5f, e = h - inset;
            for (int axis = 0; axis < 3; ++axis)
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Vector3 normal = Axis(axis, sign);
                    Vector3 u = Axis((axis + 1) % 3, e), v = Axis((axis + 2) % 3, e), center = normal * h;
                    Face(vertices, triangles, normal, center - u - v, center + u - v, center + u + v, center - u + v);
                }
            if (inset > 0)
            {
                for (int axis = 0; axis < 3; ++axis)
                    for (int a = -1; a <= 1; a += 2)
                        for (int b = -1; b <= 1; b += 2)
                        {
                            int u = (axis + 1) % 3, v = (axis + 2) % 3;
                            Vector3 p = Axis(u, a * h) + Axis(v, b * e), q = Axis(u, a * e) + Axis(v, b * h), along = Axis(axis, e);
                            Face(vertices, triangles, Axis(u, a) + Axis(v, b), p - along, p + along, q + along, q - along);
                        }
                for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                        for (int z = -1; z <= 1; z += 2)
                            Face(vertices, triangles, new Vector3(x, y, z), new Vector3(x * h, y * e, z * e), new Vector3(x * e, y * h, z * e), new Vector3(x * e, y * e, z * h));
            }
            return MeshFrom(vertices, triangles, inset > 0 ? "Original chamfered box" : "Unit box");
        }
        static Vector3 Axis(int axis, float amount) => axis == 0 ? new Vector3(amount, 0, 0) : axis == 1 ? new Vector3(0, amount, 0) : new Vector3(0, 0, amount);
        static Mesh Lathe(Vector2[] profile, int segments, string name)
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            for (int j = 0; j < profile.Length - 1; ++j)
                for (int i = 0; i < segments; ++i)
                {
                    float a = i * Mathf.PI * 2 / segments, b = (i + 1) * Mathf.PI * 2 / segments;
                    Vector3 p = Revolve(profile[j], a), q = Revolve(profile[j], b), r = Revolve(profile[j + 1], b), s = Revolve(profile[j + 1], a);
                    Vector2 tangent = profile[j + 1] - profile[j];
                    Vector3 normal = new Vector3(Mathf.Cos((a + b) * .5f) * tangent.y, -tangent.x, Mathf.Sin((a + b) * .5f) * tangent.y);
                    if (profile[j].x < .0001f) Face(vertices, triangles, normal, p, r, s);
                    else if (profile[j + 1].x < .0001f) Face(vertices, triangles, normal, p, q, r);
                    else Face(vertices, triangles, normal, p, q, r, s);
                }
            return MeshFrom(vertices, triangles, name);
        }
        static Vector3 Revolve(Vector2 point, float angle) => new Vector3(Mathf.Cos(angle) * point.x, point.y, Mathf.Sin(angle) * point.x);
        static void Face(List<Vector3> vertices, List<int> triangles, Vector3 outward, params Vector3[] points)
        {
            bool reverse = Vector3.Dot(Vector3.Cross(points[1] - points[0], points[2] - points[0]), outward) < 0;
            int start = vertices.Count; vertices.AddRange(points);
            for (int i = 1; i < points.Length - 1; ++i)
            {
                triangles.Add(start); triangles.Add(start + (reverse ? i + 1 : i)); triangles.Add(start + (reverse ? i : i + 1));
            }
        }
        static Mesh MeshFrom(List<Vector3> vertices, List<int> triangles, string name)
        {
            Mesh mesh = new Mesh { name = name }; mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        void OnDestroy()
        {
            // Factories share their small immutable mesh/material cache across sessions and carry views.
            // Only this world's hierarchy belongs to this component.
            if (hotelRoot != null) Retire(hotelRoot.gameObject);
        }
    }

    /// <summary>Lightweight procedural rig: no Animator controller or imported animation dependency.</summary>
    internal sealed class HotelFigure : MonoBehaviour
    {
        internal Transform body, head, leftArm, rightArm, leftLeg, rightLeg, mouth, caption;
        internal float phase;
        internal bool annoyed;
        Vector3 desired;
        float desiredYaw, pitch, stride;
        bool initialized, carrying, working;
        Camera facingCamera;

        internal void Pose(Vector3 position, float? yaw, float lookPitch, bool carry, bool work)
        {
            if (!initialized)
            {
                desired = position; transform.position = position; desiredYaw = yaw ?? 180;
                transform.rotation = Quaternion.Euler(0, desiredYaw, 0); initialized = true;
            }
            Vector3 delta = position - desired;
            if (yaw.HasValue) desiredYaw = yaw.Value;
            else if (delta.x * delta.x + delta.z * delta.z > .0000001f) desiredYaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            desired = position; pitch = Mathf.Clamp(lookPitch, -55, 55); carrying = carry; working = work;
        }

        void LateUpdate()
        {
            if (!initialized) return;
            float dt = Mathf.Min(Time.deltaTime, .1f);
            float blend = 1 - Mathf.Exp(-18 * dt);
            float distance = (desired - transform.position).magnitude;
            if (distance > 3) transform.position = desired;
            else transform.position = Vector3.Lerp(transform.position, desired, blend);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, desiredYaw, 0), blend);
            stride = Mathf.Lerp(stride, Mathf.Clamp01(distance * 9), blend);
            phase += dt * Mathf.Lerp(1.1f, 8.5f, stride);
            float swing = Mathf.Sin(phase) * 26 * stride;
            leftLeg.localRotation = Quaternion.Euler(swing, 0, 0);
            rightLeg.localRotation = Quaternion.Euler(-swing, 0, 0);
            leftArm.localRotation = Quaternion.Euler(carrying ? -52 : -swing * .7f, 0, 7);
            rightArm.localRotation = Quaternion.Euler(working ? -65 + Mathf.Sin(Time.time * 12) * 15 : carrying ? -63 : swing * .7f, 0, -7);
            body.localPosition = Vector3.up * (Mathf.Abs(Mathf.Sin(phase)) * .026f * stride + Mathf.Sin(Time.time * 1.8f + phase) * .004f);
            head.localRotation = Quaternion.Euler(pitch * .5f, Mathf.Sin(Time.time * .8f + phase) * (stride < .1f ? 5 : 0), annoyed ? 7 : 0);
            mouth.localRotation = Quaternion.Euler(0, 0, annoyed ? -18 : 0);
            if (caption != null)
            {
                if (facingCamera == null || !facingCamera.isActiveAndEnabled) facingCamera = Camera.main;
                if (facingCamera != null)
                {
                    Vector3 towardCamera = facingCamera.transform.position - caption.position; towardCamera.y = 0;
                    if (towardCamera.sqrMagnitude > .001f) caption.rotation = Quaternion.LookRotation(-towardCamera, Vector3.up);
                }
            }
        }
    }

    internal sealed class HotelLeak : MonoBehaviour
    {
        internal Transform[] drops;
        void Update()
        {
            if (drops == null) return;
            for (int i = 0; i < drops.Length; ++i)
            {
                float phase = Mathf.Repeat(Time.time * 1.5f + i * .2f, 1);
                drops[i].localPosition = new Vector3(.3f + phase * .1f, -.26f - phase * .68f, .41f);
            }
        }
    }
}
