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
        readonly HashSet<int> seenRooms = new HashSet<int>();
        readonly HashSet<int> coffeeDelivered = new HashSet<int>();
        readonly Dictionary<int, string> guestRequests = new Dictionary<int, string>();
        Transform mvpServices;
        EquipmentVisual waterService, powerService;
        GameObject basicCoffee, upgradedCoffee;
        TextMesh coffeeStock;
        int previousCoffee = int.MinValue;

        sealed class RoomVisual
        {
            public GameObject clean, dirty, towel, trash, leak, upgraded;
            public Transform water;
            public Renderer lamp;
            public TextMesh status;
            public int bed = -1, flag = -1;
            public float waterAmount = -1;
            public GameObject furnishings, conditions, lockedDoor, openDoor, legacyFixtures, mvpFixtures;
            public Transform bedGeometry;
            public Renderer bedFabric, linenBand, finishWall, finishRug, tvBezel, lampGlow, ceilingGlow;
            public GameObject luxuryLinen, tvUpgrade, deliveredCoffee;
            public readonly List<GameObject> dirt = new List<GameObject>(), dirtyTowels = new List<GameObject>(), overflow = new List<GameObject>();
            public readonly Dictionary<string, EquipmentVisual> equipment = new Dictionary<string, EquipmentVisual>();
            public TextMesh roomInfo;
            public Light roomLight;
            public bool owned = true, ownershipKnown;
            public int capacity = -1, bedQuality = -1, tvQuality = -1;
            public string finish;
        }
        sealed class EquipmentVisual
        {
            public GameObject root, model, failure, unavailable, missing, working;
            public Renderer indicator;
            public Renderer surface;
            public TextMesh status;
            public readonly List<GameObject> wear = new List<GameObject>();
            public int code = -1;
        }
        sealed class PersonVisual
        {
            public GameObject root;
            public HotelFigure figure;
            public TextMesh caption;
            public string stage, guestName, request;
            public int room = -1, party = -1;
            public bool annoyed, luggage, towel;
        }
        sealed class ItemVisual
        {
            public GameObject root;
            public Collider[] colliders;
            public string kind;
            public bool held;
            public TextMesh ownerTag;
            public int ownerGuest = -1;
            public string size, condition;
            public Renderer[] surfaces;
            public Material[] originalMaterials;
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
            BuildServices();
            for (int n = HotelLayout.FirstRoom; n <= HotelLayout.LastRoom; ++n) BuildRoom(n);
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
            ApplyServices(state);
            coffeeDelivered.Clear();
            guestRequests.Clear();
            if (state.mvp != null && state.mvp.requests != null)
                foreach (MvpRequestState request in state.mvp.requests)
                {
                    if (request == null) continue;
                    if (request.kind == "coffee" && request.status == "fulfilled") coffeeDelivered.Add(request.guestId);
                    if (request.status == "open" && !guestRequests.ContainsKey(request.guestId)) guestRequests.Add(request.guestId, request.kind);
                }
            seenRooms.Clear();
            if (state.rooms != null)
                foreach (RoomState room in state.rooms)
                    if (room != null && seenRooms.Add(room.number) && rooms.TryGetValue(room.number, out RoomVisual visual)) ApplyRoom(state, room, visual);
            foreach (var pair in rooms)
                if (!seenRooms.Contains(pair.Key)) SetOwned(pair.Value, pair.Key <= 104);

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
                        int appearance = guest.mvp == null ? guest.id : unchecked(guest.id * 4) +
                            (guest.mvp.archetype == "business" ? 0 : guest.mvp.archetype == "family" ? 2 : guest.mvp.archetype == "vip" ? 3 : 1);
                        visual = CreatePerson(false, appearance, "guest_" + guest.id, "Гость");
                        guests.Add(guest.id, visual);
                    }
                    visual.figure.Pose(guest.position, null, 0, false, false);
                    guestRequests.TryGetValue(guest.id, out string requestKind);
                    visual.figure.React(guest.stage, guest.satisfaction, guest.towelRequested || !string.IsNullOrEmpty(requestKind));
                    int partySize = guest.mvp == null ? 1 : guest.mvp.partySize;
                    bool annoyed = guest.satisfaction < 45;
                    if (visual.stage != guest.stage || visual.guestName != guest.name || visual.request != requestKind || visual.room != guest.room ||
                        visual.party != partySize || visual.annoyed != annoyed || visual.luggage != guest.luggageDelivered || visual.towel != guest.towelRequested)
                    {
                        string caption = GuestCaption(guest, requestKind);
                        visual.caption.text = caption;
                        visual.caption.color = annoyed ? ColorOf(Ink.Red) : ColorOf(Ink.Dark);
                        visual.stage = guest.stage; visual.guestName = guest.name; visual.request = requestKind; visual.room = guest.room;
                        visual.party = partySize; visual.annoyed = annoyed; visual.luggage = guest.luggageDelivered; visual.towel = guest.towelRequested;
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
                        visual.surfaces = root.GetComponentsInChildren<Renderer>(true);
                        visual.originalMaterials = new Material[visual.surfaces.Length];
                        for (int j = 0; j < visual.surfaces.Length; ++j) visual.originalMaterials[j] = visual.surfaces[j].sharedMaterial;
                        if (item.kind == "bag")
                            visual.ownerTag = Text(root.transform, "", new Vector3(0, .4f, -.218f), .065f, Ink.White);
                        items[item.id] = visual;
                    }
                    if (visual.size != item.size)
                    {
                        visual.root.transform.localScale = item.kind == "bag" && item.size == "large" ? new Vector3(1.3f, 1.35f, 1.25f) : Vector3.one;
                        visual.size = item.size;
                    }
                    if (visual.condition != item.condition)
                    {
                        bool textile = item.kind == "towel" || item.kind == "linen";
                        for (int j = 0; j < visual.surfaces.Length; ++j)
                            if (visual.surfaces[j].GetComponent<TextMesh>() == null)
                                visual.surfaces[j].sharedMaterial = textile && item.condition == "dirty" ? Mat(Ink.Dirty) : visual.originalMaterials[j];
                        if (textile) SetTargetLabel(visual.root, item.condition == "dirty" ? (item.kind == "towel" ? "Грязное полотенце → корзина" : "Грязное бельё → корзина") : ItemLabel(item.kind));
                        visual.condition = item.condition;
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
                        at.y = item.placedRoom > 0 ? HotelLayout.RoomTarget(item.kind == "coffee" || item.kind == "coffeecup" ? "coffee" : "bag", item.placedRoom).y + .015f : item.placedRoom == -1 ? .325f : .025f;
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
            Box(p, "Hall floor", new Vector3(0, -.12f, 12.5f), new Vector3(3, .24f, 21), Ink.Oak, true);
            Rug(p, new Vector3(0, .02f, 12.45f), new Vector2(2.1f, 20.6f));
            for (int z = 3; z < 23; z += 2)
            {
                GameObject diamond = Box(p, "Runner diamond", new Vector3(0, .039f, z), new Vector3(.22f, .008f, .22f), Ink.Brass);
                diamond.transform.localRotation = Quaternion.Euler(0, 45, 0);
            }
            Wall(p, new Vector3(-7.9f, 0, 8), new Vector3(.2f, 3.6f, 30));
            Wall(p, new Vector3(7.9f, 0, 8), new Vector3(.2f, 3.6f, 30));
            Wall(p, new Vector3(0, 0, 23.1f), new Vector3(16, 3.6f, .2f));
            Wall(p, new Vector3(-4.95f, 0, -7), new Vector3(5.9f, 3.6f, .2f));
            Wall(p, new Vector3(4.95f, 0, -7), new Vector3(5.9f, 3.6f, .2f));
            Box(p, "Entrance lintel", new Vector3(0, 3.17f, -7), new Vector3(4, .86f, .24f), Ink.Wood, true);
            // An open porch is part of the navigable floor, with a physical boundary beyond it.
            Box(p, "Porch", new Vector3(0, -.16f, -8.25f), new Vector3(8, .32f, 2.5f), Ink.Ceramic, true);
            Box(p, "Porch back rail", new Vector3(0, .5f, -9.55f), new Vector3(8.1f, 1, .16f), Ink.Wood, true);
            Box(p, "Porch left rail", new Vector3(-4.05f, .5f, -8.25f), new Vector3(.16f, 1, 2.6f), Ink.Wood, true);
            Box(p, "Porch right rail", new Vector3(4.05f, .5f, -8.25f), new Vector3(.16f, 1, 2.6f), Ink.Wood, true);
            Box(p, "Lobby ceiling", new Vector3(0, 3.65f, -2.5f), new Vector3(15.8f, .18f, 9), Ink.Cream, true);
            Box(p, "Hall ceiling", new Vector3(0, 3.65f, 12.5f), new Vector3(3, .18f, 21), Ink.Cream, true);
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Wall(p, new Vector3(sign * 4.7f, 0, 2), new Vector3(6.4f, 3.6f, .18f));
                Wall(p, new Vector3(sign * 4.7f, 0, 9), new Vector3(6.4f, 3.6f, .18f));
                Wall(p, new Vector3(sign * 4.7f, 0, 16), new Vector3(6.4f, 3.6f, .18f));
                foreach (float z in new[] { 3.375f, 9f, 16f, 21.625f })
                    Wall(p, new Vector3(sign * 1.5f, 0, z), new Vector3(.18f, 3.6f, z == 9 || z == 16 ? 5.5f : 2.75f));
                foreach (float z in new[] { 5.5f, 12.5f, 19.5f })
                    Box(p, "Portal lintel", new Vector3(sign * 1.5f, 3.12f, z), new Vector3(.24f, .96f, 1.5f), Ink.Plaster, true);
                for (int z = 3; z <= 22; z += 7)
                {
                    Frame(p, new Vector3(sign * 1.387f, 2.05f, z + .5f), sign * 90, z == 3 ? "ТИШЕ ЕДЕШЬ" : "СЛАДКИХ СНОВ", .83f, .76f);
                }
            }
            Frame(p, new Vector3(0, 2, 22.97f), 0, "ГРАНД ОТЕЛЬ\nЗДЕСЬ СТАНОВИТСЯ ЛУЧШЕ", 2.2f, 1.1f);
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

        void BuildServices()
        {
            mvpServices = Group(living, "MVP service stations");
            Transform utility = Group(mvpServices, "Southwest water and power station");
            Box(utility, "Utility station backboard", new Vector3(-6.8f, 1.15f, -5.66f), new Vector3(1.97f, 1.8f, .12f), Ink.Wood, true);
            Text(utility, "ТЕХНИЧЕСКАЯ СЛУЖБА", new Vector3(-6.8f, 2.2f, -5.57f), .115f, Ink.Cream, 180);
            Vector3 waterAt = HotelLayout.Target("utility_water");
            Transform water = Group(utility, "Water main", waterAt); water.localRotation = Quaternion.Euler(0, 180, 0);
            Tube(water, "Water main pipe", new Vector3(0, -.68f, .1f), new Vector3(0, .43f, .1f), .095f, Ink.Metal);
            GameObject wheel = Cylinder(water, "Water shutoff wheel", new Vector3(0, 0, -.06f), new Vector3(.38f, .075f, .38f), Ink.Teal, true);
            wheel.transform.localRotation = Quaternion.Euler(90, 0, 0);
            for (int i = -1; i <= 1; i += 2)
                Tube(water, "Valve wheel spokes", new Vector3(i * -.16f, -.16f, -.11f), new Vector3(i * .16f, .16f, -.11f), .035f, Ink.Brass);
            Target(water.gameObject, "utility_water", "Общая подача воды — ремонт");
            waterService = BuildEquipmentPanel(utility, "utility_water", water.gameObject, waterAt, new Vector3(0, .62f, .08f));
            waterService.root.transform.localRotation = Quaternion.Euler(0, 180, 0);
            Vector3 powerAt = HotelLayout.Target("utility_power");
            Transform power = Group(utility, "Power distribution", powerAt); power.localRotation = Quaternion.Euler(0, 180, 0);
            Rounded(power, "Fuse cabinet", Vector3.zero, new Vector3(.57f, .82f, .23f), Ink.Metal, true);
            for (int i = 0; i < 3; ++i)
                Rounded(power, "Breaker switch", new Vector3((i - 1) * .16f, .04f, -.15f), new Vector3(.085f, .23f, .055f), Ink.Dark);
            Text(power, "220 В", new Vector3(0, -.24f, -.143f), .09f, Ink.Gold);
            Target(power.gameObject, "utility_power", "Общее питание — ремонт");
            powerService = BuildEquipmentPanel(utility, "utility_power", power.gameObject, powerAt, new Vector3(0, .62f, .08f));
            powerService.root.transform.localRotation = Quaternion.Euler(0, 180, 0);

            Transform coffee = Group(mvpServices, "Lobby coffee station", HotelLayout.Target("coffee"));
            Rounded(coffee, "Coffee cabinet", new Vector3(0, -.55f, 0), new Vector3(1.2f, .86f, .67f), Ink.Teal, true);
            Rounded(coffee, "Coffee counter", new Vector3(0, -.06f, 0), new Vector3(1.3f, .12f, .79f), Ink.Oak, true);
            Transform basic = Group(coffee, "Manual coffee service"); basicCoffee = basic.gameObject;
            Cylinder(basic, "Insulated coffee pot", new Vector3(-.25f, .23f, .02f), new Vector3(.32f, .43f, .32f), Ink.Metal, true);
            Cylinder(basic, "Coffee pot lid", new Vector3(-.25f, .46f, .02f), new Vector3(.36f, .045f, .36f), Ink.Dark);
            Handle(basic, new Vector3(-.48f, .14f, .03f), .12f, .19f, Ink.Dark);
            Tube(basic, "Coffee pot spout", new Vector3(-.14f, .38f, .02f), new Vector3(-.04f, .43f, -.1f), .065f, Ink.Metal);
            Transform machine = Group(coffee, "Upgraded coffee machine"); upgradedCoffee = machine.gameObject;
            Rounded(machine, "Espresso machine", new Vector3(-.16f, .29f, .05f), new Vector3(.63f, .55f, .45f), Ink.Burgundy, true);
            Box(machine, "Coffee machine front", new Vector3(-.16f, .27f, -.193f), new Vector3(.52f, .36f, .03f), Ink.Metal);
            Tube(machine, "Coffee nozzle", new Vector3(-.16f, .25f, -.23f), new Vector3(-.16f, .14f, -.23f), .045f, Ink.Brass);
            Ball(machine, "Coffee ready light", new Vector3(.02f, .37f, -.225f), Vector3.one * .047f, Ink.Green);
            for (int i = 0; i < 3; ++i) Cup(coffee, new Vector3(.25f, .017f + i * .06f, .03f));
            Text(coffee, "КОФЕ ДЛЯ ГОСТЕЙ", new Vector3(0, .88f, -.03f), .11f, Ink.Burgundy);
            coffeeStock = Text(coffee, "", new Vector3(0, -.25f, -.35f), .074f, Ink.Cream);
            Target(coffee.gameObject, "coffee", "Приготовить кофе гостю");
            SetActive(upgradedCoffee, false); mvpServices.gameObject.SetActive(false);
        }

        void ApplyServices(HotelState state)
        {
            bool mvp = state.mvp != null; SetActive(mvpServices.gameObject, mvp);
            if (!mvp) return;
            MvpUtilityState utility = state.mvp.utilities;
            ApplyUtility(waterService, utility != null && utility.waterFault, utility == null ? 0 : utility.waterWear, "НЕТ ВОДЫ");
            ApplyUtility(powerService, utility != null && utility.powerFault, utility == null ? 0 : utility.powerWear, "НЕТ ПИТАНИЯ");
            SetActive(basicCoffee, !state.mvp.coffeeMachine); SetActive(upgradedCoffee, state.mvp.coffeeMachine);
            if (previousCoffee != state.mvp.coffeeStock)
            {
                coffeeStock.text = "ПОРЦИЙ: " + state.mvp.coffeeStock;
                previousCoffee = state.mvp.coffeeStock;
            }
        }

        static void ApplyUtility(EquipmentVisual visual, bool fault, float wear, string label)
        {
            SetActive(visual.failure, fault);
            int code = fault ? 2 : 0;
            if (visual.code != code)
            {
                visual.status.text = fault ? label : "ПОДАЧА ЕСТЬ";
                visual.indicator.sharedMaterial = Mat(fault ? Ink.Red : Ink.Green); visual.code = code;
            }
            for (int i = 0; i < visual.wear.Count; ++i) SetActive(visual.wear[i], wear > (i + 1) * .25f);
        }

        void BuildRoom(int n)
        {
            Vector3 c = HotelLayout.RoomCenter(n);
            float s = n % 2 == 1 ? -1 : 1;
            Transform p = Group(architecture, "Room " + n);
            Transform dynamicRoom = Group(living, "Room " + n + " / conditions");
            RoomVisual visual = new RoomVisual(); rooms.Add(n, visual);
            visual.conditions = dynamicRoom.gameObject;
            Box(p, "Bedroom floor", c + new Vector3(0, -.12f, 0), new Vector3(6.25f, .24f, 6.85f), Ink.Oak, true);
            Box(p, "Bedroom ceiling", c + new Vector3(0, 3.65f, 0), new Vector3(6.25f, .18f, 6.85f), Ink.Cream, true);
            for (int i = 0; i < 8; ++i)
                Box(p, "Bedroom floorboard seam", c + new Vector3(s * (-2.7f + i * .76f), .002f, 0), new Vector3(.014f, .004f, 6.7f), Ink.Wood);
            Rug(p, c + new Vector3(s * .35f, .014f, 1.16f), new Vector2(3.5f, 2.2f));
            for (int x = 0; x < 5; ++x)
                for (int z = 0; z < 3; ++z)
                    Box(p, "Washroom tile", c + new Vector3(s * (-1.8f + x * .91f), .01f, -2.98f + z * .64f),
                        new Vector3(.88f, .016f, .61f), (x + z) % 2 == 0 ? Ink.Ceramic : Ink.Cream);
            p = Group(living, "Room " + n + " / furnishings");
            visual.furnishings = p.gameObject;
            // Bed runs east/west. Its nearest edge is z = centre + .36, leaving the NPC centreline clear.
            Vector3 bedAt = HotelLayout.RoomTarget("bed", n);
            Transform bedTarget = Group(dynamicRoom, "Bed " + n, bedAt);
            Transform bed = Group(bedTarget, "Capacity geometry");
            visual.bedGeometry = bed;
            Rounded(bed, "Timber bed frame", new Vector3(0, -.34f, 0), new Vector3(2.65f, .28f, 1.47f), Ink.Wood, true);
            Rounded(bed, "Mattress ticking", new Vector3(0, -.1f, 0), new Vector3(2.5f, .29f, 1.42f), Ink.Linen, true);
            Rounded(bed, "Scalloped headboard", new Vector3(s * 1.24f, .02f, 0), new Vector3(.16f, 1.2f, 1.54f), Ink.Wood, true);
            for (int j = -1; j <= 1; ++j)
            {
                Renderer fabric = Rounded(bed, "Upholstered headboard inset", new Vector3(s * 1.135f, .2f, j * .44f), new Vector3(.052f, .57f, .34f), Ink.Burgundy).GetComponent<Renderer>();
                if (j == 0) visual.bedFabric = fabric;
            }
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    Cylinder(bed, "Turned bed foot", new Vector3(x * 1.08f, -.55f, z * .58f), new Vector3(.13f, .3f, .13f), Ink.Wood);
            Target(bedTarget.gameObject, "bed_" + n, "Кровать — снять / застелить бельё");
            Transform clean = Group(bed, "Clean bedding");
            Rounded(clean, "Clean duvet", new Vector3(-s * .17f, .075f, 0), new Vector3(2.1f, .19f, 1.4f), Ink.White);
            visual.linenBand = Rounded(clean, "Burgundy runner", new Vector3(-s * .78f, .185f, 0), new Vector3(.43f, .045f, 1.43f), Ink.Burgundy).GetComponent<Renderer>();
            for (int z = -1; z <= 1; z += 2)
                Rounded(clean, "Plump pillow", new Vector3(s * .9f, .125f, z * .35f), new Vector3(.5f, .24f, .57f), Ink.White);
            visual.clean = clean.gameObject;
            Transform dirty = Group(bed, "Dirty bedding");
            Rounded(dirty, "Crumpled sheet", new Vector3(-s * .21f, .085f, .01f), new Vector3(1.99f, .18f, 1.36f), Ink.Dirty);
            for (int j = 0; j < 5; ++j)
            {
                GameObject fold = Rounded(dirty, "Rumpled blanket fold", new Vector3(-.77f + j * .35f, .2f, .05f), new Vector3(.33f, .18f, 1.12f), j % 2 == 0 ? Ink.Linen : Ink.Dirty);
                fold.transform.localRotation = Quaternion.Euler(0, (j % 2 == 0 ? -12 : 9), 8);
            }
            Ball(dirty, "Tea stain", new Vector3(.25f, .304f, -.35f), new Vector3(.36f, .014f, .26f), Ink.Wood);
            Rounded(dirty, "Forgotten pillow", new Vector3(s * .83f, .15f, .35f), new Vector3(.55f, .24f, .55f), Ink.Linen).transform.localRotation = Quaternion.Euler(0, 24, 0);
            visual.dirty = dirty.gameObject;
            Transform upgraded = Group(bed, "Bed upgrade / brass finials");
            for (int z = -1; z <= 1; z += 2)
            {
                Cylinder(upgraded, "Upgrade headboard post", new Vector3(s * 1.23f, .22f, z * .77f), new Vector3(.08f, 1.27f, .08f), Ink.Brass);
                Ball(upgraded, "Upgrade finial", new Vector3(s * 1.23f, .9f, z * .77f), Vector3.one * .17f, Ink.Brass);
            }
            visual.upgraded = upgraded.gameObject;
            Transform luxury = Group(clean, "Premium linen embroidery");
            for (int j = -1; j <= 1; j += 2)
                Box(luxury, "Linen embroidered edge", new Vector3(-s * .12f, .18f, j * .57f), new Vector3(1.86f, .012f, .023f), Ink.Brass);
            visual.luxuryLinen = luxury.gameObject; SetActive(visual.luxuryLinen, false);

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
            visual.equipment["sink"] = BuildEquipmentPanel(dynamicRoom, "sink_" + n, sink.gameObject, sinkAt, new Vector3(0, .62f, .07f));
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

            Transform legacy = Group(p, "Legacy room fixtures"); visual.legacyFixtures = legacy.gameObject;
            Toilet(legacy, c + new Vector3(-s * 1.65f, 0, -2.7f));
            Rounded(legacy, "Bedside cabinet", c + new Vector3(s * 2.05f, .37f, 2.46f), new Vector3(1.1f, .74f, .76f), Ink.Wood, true);
            Lamp(legacy, c + new Vector3(s * 2.05f, 1.27f, 2.46f), .34f);
            Cup(legacy, c + new Vector3(s * 1.8f, .78f, 2.46f));
            RoomWindow(p, c + new Vector3(s * 3.075f, 2.04f, .1f), s);
            Frame(p, c + new Vector3(s * .1f, 2.22f, 3.375f), 0, n % 2 == 0 ? "ВЫ ПРЕКРАСНО\nВЫГЛЯДИТЕ.\nОТЕЛЬ СТАРАЕТСЯ." : "МОРЕ ДАЛЕКО.\nЗАТО КРОВАТЬ\nБЛИЗКО.", 1.4f, 1.06f);
            // A tiny television on the inner wall leaves the crossing route unobstructed.
            Transform tv = Group(legacy, "Old television", c + new Vector3(-s * 2.96f, 1.9f, 2.26f));
            tv.localRotation = Quaternion.Euler(0, -s * 90, 0);
            Rounded(tv, "TV casing", Vector3.zero, new Vector3(1.13f, .73f, .2f), Ink.Dark);
            Rounded(tv, "TV sleepy screen", new Vector3(-.055f, .02f, -.12f), new Vector3(.86f, .52f, .03f), Ink.Teal);
            Text(tv, "НЕТ СИГНАЛА", new Vector3(-.055f, .02f, -.144f), .063f, Ink.Foam);
            BuildRoomMvp(n, visual, dynamicRoom);

            Vector3 doorAt = HotelLayout.Door(n);
            Transform door = Group(architecture, "Room portal " + n, doorAt);
            for (int j = -1; j <= 1; j += 2)
                Box(door, "Portal jamb outside clear opening", new Vector3(0, 1.31f, j * .805f), new Vector3(.32f, 2.62f, .11f), Ink.Wood, true);
            Box(door, "Portal crown", new Vector3(0, 2.66f, 0), new Vector3(.35f, .15f, 1.74f), Ink.Oak, true);
            // Leaf is parked fully inside the room, parallel to the walking axis, never across the opening.
            Transform openLeaf = Group(living, "Open door " + n, doorAt); visual.openDoor = openLeaf.gameObject;
            Rounded(openLeaf, "Door leaf fixed open", new Vector3(s * .84f, 1.23f, .88f), new Vector3(1.47f, 2.46f, .12f), Ink.Wood, true);
            Box(openLeaf, "Open leaf inset", new Vector3(s * .84f, 1.31f, .801f), new Vector3(1.15f, 1.83f, .035f), Ink.Oak);
            Ball(openLeaf, "Door handle", new Vector3(s * 1.39f, 1.06f, .765f), Vector3.one * .085f, Ink.Brass);
            Target(openLeaf.gameObject, "door_" + n, "Номер " + n + " — доступность");
            Transform locked = Group(living, "Locked door " + n, HotelLayout.RoomTarget("door", n));
            Rounded(locked, "Locked room barrier", new Vector3(0, .3f, 0), new Vector3(.2f, 2.6f, 1.5f), Ink.Wood, true);
            Box(locked, "Locked room brass band", new Vector3(-s * .115f, .3f, 0), new Vector3(.027f, .15f, 1.34f), Ink.Brass);
            Text(locked, "КРЫЛО ЗАКРЫТО\nПОКУПКА НА ДОСКЕ", new Vector3(-s * .132f, .77f, 0), .082f, Ink.Cream, s * 90);
            Target(locked.gameObject, "door_" + n, "Номер " + n + " — требуется покупка");
            visual.lockedDoor = locked.gameObject;
            Rounded(door, "Room number plaque", new Vector3(-s * .165f, 1.74f, 1.04f), new Vector3(.04f, .49f, .61f), Ink.Burgundy, true);
            Text(door, n.ToString(), new Vector3(-s * .19f, 1.77f, 1.04f), .205f, Ink.Cream, s * 90);
            Target(door.gameObject, "door_" + n, "Номер " + n + " — доступность");
            visual.status = Text(living, "ГОТОВ", doorAt + new Vector3(-s * .197f, 1.4f, 1.04f), .064f, Ink.Teal, s * 90);
            visual.roomInfo = Text(living, "", doorAt + new Vector3(-s * .197f, 1.25f, 1.04f), .045f, Ink.Dark, s * 90);
            visual.lamp = Ball(living, "Room status lamp", doorAt + new Vector3(-s * .191f, 2.09f, 1.04f), new Vector3(.075f, .1f, .1f), Ink.Green).GetComponent<Renderer>();
            Transform ceiling = Lamp(p, c + new Vector3(0, 3.15f, -.3f), .57f);
            visual.ceilingGlow = ceiling.Find("Warm bulb").GetComponent<Renderer>();
            SetActive(visual.dirty, false); SetActive(visual.leak, false); SetActive(visual.trash, false);
            SetActive(visual.upgraded, false); SetActive(visual.water.gameObject, false);
            SetOwned(visual, n <= 104);
        }

        void BuildRoomMvp(int n, RoomVisual v, Transform conditions)
        {
            Vector3 c = HotelLayout.RoomCenter(n); float s = n % 2 == 1 ? -1 : 1;
            Transform p = Group(conditions, "MVP room fixtures " + n); v.mvpFixtures = p.gameObject;
            Vector3 toiletAt = HotelLayout.RoomTarget("toilet", n);
            Transform toilet = Group(p, "Toilet " + n, toiletAt);
            Toilet(toilet, new Vector3(0, -.55f, 0));
            Target(toilet.gameObject, "toilet_" + n, "Унитаз — вантуз / ремонт");
            v.equipment["toilet"] = BuildEquipmentPanel(p, "toilet_" + n, toilet.gameObject, toiletAt, new Vector3(0, .8f, -.18f));
            Ball(v.equipment["toilet"].failure.transform, "Blocked toilet contents", new Vector3(0, -.8f, .34f), new Vector3(.3f, .035f, .38f), Ink.Dirty);
            // TV is suspended above walking height. Its fixture root retains the exact logical target.
            Vector3 tvAt = HotelLayout.RoomTarget("tv", n);
            Transform tv = Group(p, "Television " + n, tvAt);
            Vector3 screenAt = new Vector3(s * .4f, .96f, .45f);
            v.tvBezel = Rounded(tv, "TV case", screenAt, new Vector3(1.1f, .72f, .19f), Ink.Dark, true).GetComponent<Renderer>();
            Box(tv, "Ceiling TV bracket", screenAt + new Vector3(0, .55f, .04f), new Vector3(.08f, .47f, .08f), Ink.Metal, true);
            Renderer screen = Rounded(tv, "Powered TV screen", screenAt + new Vector3(0, 0, -.112f), new Vector3(.91f, .54f, .028f), Ink.Teal).GetComponent<Renderer>();
            Target(tv.gameObject, "tv_" + n, "Телевизор — ремонт");
            EquipmentVisual television = BuildEquipmentPanel(p, "tv_" + n, tv.gameObject, tvAt, new Vector3(s * .4f, .59f, .3f));
            v.equipment["tv"] = television;
            Transform broadcast = Group(tv, "TV broadcast", screenAt + new Vector3(0, 0, -.138f));
            Text(broadcast, "ГРАНД-ТВ", new Vector3(0, .1f, -.008f), .11f, Ink.Foam);
            for (int i = 0; i < 4; ++i)
                Box(broadcast, "Broadcast colour bar", new Vector3(-.3f + .2f * i, -.12f, 0), new Vector3(.19f, .13f, .008f), i % 2 == 0 ? Ink.Gold : Ink.Blue);
            television.working = broadcast.gameObject;
            television.surface = screen;
            Transform tvTrim = Group(tv, "Upgraded TV trim", screenAt);
            for (int side = -1; side <= 1; side += 2)
                Box(tvTrim, "TV quality trim", new Vector3(side * .54f, 0, -.106f), new Vector3(.025f, .64f, .024f), Ink.Brass);
            v.tvUpgrade = tvTrim.gameObject;
            // A wall shelf leaves the enlarged bed and the north-side approach clear.
            Vector3 lampAt = HotelLayout.RoomTarget("lamp", n);
            Transform lamp = Group(p, "Room lamp " + n, lampAt);
            Box(lamp, "Lamp wall shelf", new Vector3(s * .25f, -.44f, 0), new Vector3(.57f, .08f, .46f), Ink.Oak, true);
            Transform shade = Lamp(lamp, new Vector3(s * .25f, 0, 0), .34f);
            v.lampGlow = shade.Find("Warm bulb").GetComponent<Renderer>();
            BoxCollider lampHit = shade.gameObject.AddComponent<BoxCollider>(); lampHit.size = new Vector3(.37f, .28f, .37f);
            Target(lamp.gameObject, "lamp_" + n, "Лампа — ремонт");
            v.equipment["lamp"] = BuildEquipmentPanel(p, "lamp_" + n, lamp.gameObject, lampAt, new Vector3(0, .36f, 0));

            Vector3 coffeeAt = HotelLayout.RoomTarget("coffee", n);
            Transform coffee = Group(p, "Coffee service " + n, coffeeAt);
            Rounded(coffee, "Coffee table top", new Vector3(0, -.065f, 0), new Vector3(.62f, .1f, .55f), Ink.Oak, true);
            Cylinder(coffee, "Coffee table column", new Vector3(0, -.44f, 0), new Vector3(.1f, .76f, .1f), Ink.Wood, true);
            Cylinder(coffee, "Coffee table foot", new Vector3(0, -.79f, 0), new Vector3(.43f, .08f, .43f), Ink.Wood, true);
            Text(coffee, "КОФЕ ГОСТЮ", new Vector3(0, -.055f, -.283f), .058f, Ink.Cream);
            Target(coffee.gameObject, "coffee_" + n, "Подать кофе гостю");
            Transform delivered = Group(coffee, "Delivered coffee", Vector3.up * .01f);
            Cup(delivered, Vector3.zero); v.deliveredCoffee = delivered.gameObject;
            SetActive(v.deliveredCoffee, false);

            Transform clean = Group(p, "Floor cleaning " + n, HotelLayout.RoomTarget("clean", n));
            BoxCollider cleanHit = clean.gameObject.AddComponent<BoxCollider>(); cleanHit.size = new Vector3(1.3f, .025f, .8f);
            for (int i = 0; i < 10; ++i)
            {
                float a = i * 2.39996f;
                GameObject spot = Ball(clean, "Dry dirt patch " + i, new Vector3(Mathf.Sin(a) * (i < 3 ? .34f : 1.1f), -.034f, Mathf.Cos(a) * .75f), new Vector3(.22f + (i % 3) * .08f, .015f, .18f), Ink.Dirty);
                spot.AddComponent<BoxCollider>();
                v.dirt.Add(spot); SetActive(spot, false);
            }
            Target(clean.gameObject, "clean_" + n, "Грязный пол — уборка шваброй");
            Transform laundry = Group(p, "Dirty towel collection " + n, HotelLayout.RoomTarget("dirtytowel", n));
            Rounded(laundry, "Laundry catch tray", new Vector3(0, -.15f, 0), new Vector3(.59f, .055f, .43f), Ink.Wood, true);
            for (int i = 0; i < 3; ++i)
            {
                GameObject towel = MakeItem("dirtytowel"); towel.name = "Used towel " + i; towel.transform.SetParent(laundry, false);
                towel.transform.localPosition = new Vector3((i % 2) * .025f, -.12f + i * .07f, 0);
                towel.transform.localScale = Vector3.one * .85f;
                v.dirtyTowels.Add(towel); SetActive(towel, false);
            }
            Target(laundry.gameObject, "dirtytowel_" + n, "Собрать грязные полотенца");
            Transform overflow = Group(p, "Bin overflow " + n, HotelLayout.RoomTarget("trash", n));
            for (int i = 0; i < 3; ++i)
            {
                GameObject paper = Ball(overflow, "Overflow litter " + i, new Vector3(-.3f + i * .26f, -.26f, .34f), new Vector3(.2f, .08f, .2f), Ink.Paper);
                v.overflow.Add(paper); SetActive(paper, false);
            }
            v.finishWall = Box(p, "Selectable wall finish", c + new Vector3(0, 1.75f, -3.38f), new Vector3(5.95f, 1.28f, .018f), Ink.Plaster).GetComponent<Renderer>();
            v.finishRug = Box(p, "Selectable rug inset", c + new Vector3(s * .35f, .043f, 1.16f), new Vector3(2.7f, .008f, 1.7f), Ink.Burgundy).GetComponent<Renderer>();
            SetActive(v.mvpFixtures, false);
        }

        static EquipmentVisual BuildEquipmentPanel(Transform parent, string id, GameObject model, Vector3 at, Vector3 offset)
        {
            Transform root = Group(parent, "Equipment status " + id, at);
            Transform panel = Group(root, "Service indicator", offset);
            Box(panel, "Service label backing", Vector3.zero, new Vector3(.64f, .2f, .025f), Ink.Cream, true);
            var v = new EquipmentVisual { root = root.gameObject, model = model };
            v.indicator = Ball(panel, "Equipment indicator", new Vector3(-.26f, 0, -.027f), new Vector3(.055f, .055f, .025f), Ink.Green).GetComponent<Renderer>();
            v.status = Text(panel, "ИСПРАВНО", new Vector3(.035f, 0, -.028f), .055f, Ink.Dark);
            Transform wear = Group(panel, "Wear gauge", new Vector3(0, -.14f, 0));
            for (int i = 0; i < 3; ++i)
            {
                GameObject bar = Box(wear, "Wear mark " + i, new Vector3((i - 1) * .15f, 0, 0), new Vector3(.12f, .022f, .022f), Ink.Gold);
                v.wear.Add(bar); SetActive(bar, false);
            }
            Transform failure = Group(root, "Local fault", offset);
            Tube(failure, "Fault slash", new Vector3(-.12f, .2f, 0), new Vector3(.12f, .39f, 0), .045f, Ink.Red);
            Tube(failure, "Fault slash", new Vector3(.12f, .2f, 0), new Vector3(-.12f, .39f, 0), .045f, Ink.Red);
            BoxCollider faultHit = failure.gameObject.AddComponent<BoxCollider>();
            faultHit.center = new Vector3(0, .3f, 0); faultHit.size = new Vector3(.32f, .27f, .05f);
            v.failure = failure.gameObject;
            v.unavailable = Box(root, "Utility unavailable", offset + new Vector3(0, .29f, 0), new Vector3(.31f, .1f, .035f), Ink.Gold, true);
            v.missing = Box(root, "Empty equipment slot", offset + new Vector3(0, .29f, 0), new Vector3(.36f, .18f, .035f), Ink.Dark, true);
            Target(root.gameObject, id, "Оборудование / сервис");
            SetActive(v.failure, false); SetActive(v.unavailable, false); SetActive(v.missing, false);
            return v;
        }

        static bool ApplyEquipment(EquipmentVisual v, MvpEquipmentState equipment, bool utilityFault, string utilityLabel, bool legacy = false)
        {
            bool installed = legacy || (equipment != null && equipment.installed);
            bool broken = !legacy && equipment != null && equipment.localFault;
            int code = !installed ? 3 : broken ? 2 : utilityFault ? 1 : 0;
            SetActive(v.model, installed); SetActive(v.failure, installed && broken);
            SetActive(v.unavailable, installed && utilityFault); SetActive(v.missing, !installed);
            if (v.working != null) SetActive(v.working, code == 0);
            if (v.surface != null) v.surface.sharedMaterial = Mat(code == 0 ? Ink.Teal : Ink.Dark);
            if (v.code != code)
            {
                v.status.text = code == 3 ? "НЕТ ПРИБОРА" : code == 2 ? "МЕСТНАЯ ПОЛОМКА" : code == 1 ? utilityLabel : "ИСПРАВНО";
                v.indicator.sharedMaterial = Mat(code == 3 ? Ink.Dark : code == 2 ? Ink.Red : code == 1 ? Ink.Gold : Ink.Green);
                v.code = code;
            }
            float wear = equipment == null ? 0 : Mathf.Clamp01(equipment.wear);
            for (int i = 0; i < v.wear.Count; ++i) SetActive(v.wear[i], installed && wear > (i + 1) * .25f);
            return code == 0;
        }

        static MvpEquipmentState Equipment(RoomState room, string kind)
        {
            if (room.mvp == null || room.mvp.equipment == null) return null;
            foreach (MvpEquipmentState equipment in room.mvp.equipment)
                if (equipment != null && equipment.kind == kind) return equipment;
            return null;
        }

        static void SetOwned(RoomVisual v, bool owned)
        {
            if (v.ownershipKnown && v.owned == owned) return;
            SetActive(v.furnishings, owned); SetActive(v.conditions, owned);
            SetActive(v.openDoor, owned); SetActive(v.lockedDoor, !owned);
            v.owned = owned;
            v.ownershipKnown = true;
            if (!owned)
            {
                v.status.text = "НЕ КУПЛЕН"; v.status.color = ColorOf(Ink.Gold);
                v.lamp.sharedMaterial = Mat(Ink.Gold); v.flag = -1;
                if (v.roomLight != null) v.roomLight.enabled = false;
            }
        }

        void ApplyRoom(HotelState state, RoomState room, RoomVisual v)
        {
            bool mvp = state.mvp != null && room.mvp != null;
            SetOwned(v, mvp ? room.mvp.owned : room.number <= 104);
            if (!v.owned) return;
            SetActive(v.legacyFixtures, !mvp); SetActive(v.mvpFixtures, mvp);
            SetActive(v.equipment["sink"].root, mvp);
            bool powerFault = mvp && state.mvp.utilities != null && state.mvp.utilities.powerFault;
            bool waterFault = mvp && state.mvp.utilities != null && state.mvp.utilities.waterFault;
            bool equipmentFault = false, lampWorking = true;
            foreach (var pair in v.equipment)
            {
                bool needsWater = pair.Key == "sink" || pair.Key == "toilet";
                bool working = ApplyEquipment(pair.Value, Equipment(room, pair.Key), needsWater ? waterFault : powerFault,
                    needsWater ? "НЕТ ВОДЫ" : "НЕТ ПИТАНИЯ", !mvp);
                equipmentFault |= !working;
                if (pair.Key == "lamp") lampWorking = working;
            }
            if (v.roomLight != null) v.roomLight.enabled = !powerFault && lampWorking;
            v.lampGlow.sharedMaterial = Mat(lampWorking ? Ink.Glow : Ink.Dark);
            v.ceilingGlow.sharedMaterial = Mat(lampWorking && !powerFault ? Ink.Glow : Ink.Dark);
            int capacity = mvp ? Mathf.Max(1, room.mvp.capacity) : 1;
            int bedQuality = mvp ? Mathf.Max(1, room.mvp.bedQuality) : room.upgraded ? 2 : 1;
            int tvQuality = mvp ? Mathf.Max(0, room.mvp.tvQuality) : 1;
            if (v.capacity != capacity || v.bedQuality != bedQuality || v.tvQuality != tvQuality)
            {
                v.bedGeometry.localScale = capacity >= 2 ? new Vector3(.78f, 1, 1.58f) : Vector3.one;
                v.bedGeometry.localPosition = capacity >= 2 ? new Vector3(0, 0, .48f) : Vector3.zero;
                v.bedFabric.sharedMaterial = Mat(bedQuality >= 3 ? Ink.Teal : bedQuality >= 2 ? Ink.Brass : Ink.Burgundy);
                v.tvBezel.sharedMaterial = Mat(tvQuality >= 2 ? Ink.Metal : Ink.Dark);
                SetActive(v.tvUpgrade, tvQuality >= 2);
                v.roomInfo.text = capacity + (capacity == 1 ? " МЕСТО" : " МЕСТА") + "  К" + bedQuality + " / ТВ" + tvQuality;
                v.capacity = capacity; v.bedQuality = bedQuality; v.tvQuality = tvQuality;
            }
            string finish = mvp ? room.mvp.finishId : "original";
            if (v.finish != finish)
            {
                v.finishWall.sharedMaterial = Mat(finish == "warm" ? Ink.Cream : finish == "cool" ? Ink.Blue : Ink.Plaster);
                v.finishRug.sharedMaterial = Mat(finish == "warm" ? Ink.Red : finish == "cool" ? Ink.Teal : Ink.Burgundy);
                v.finish = finish;
            }
            bool linenUpgrade = mvp && state.mvp.betterLinen;
            SetActive(v.luxuryLinen, linenUpgrade); v.linenBand.sharedMaterial = Mat(linenUpgrade ? Ink.Teal : Ink.Burgundy);
            SetActive(v.deliveredCoffee, mvp && room.guestId != 0 && coffeeDelivered.Contains(room.guestId));
            float dirtAmount = mvp ? Mathf.Clamp01(room.mvp.dirt) : 0;
            int dirtyTowels = mvp ? room.mvp.dirtyTowels : 0;
            for (int i = 0; i < v.dirt.Count; ++i) SetActive(v.dirt[i], dirtAmount > i / (float)v.dirt.Count + .001f);
            for (int i = 0; i < v.dirtyTowels.Count; ++i) SetActive(v.dirtyTowels[i], dirtyTowels > i);
            for (int i = 0; i < v.overflow.Count; ++i) SetActive(v.overflow[i], mvp && room.mvp.binFill > .65f + i * .11f);
            if (v.bed != room.bed)
            {
                SetActive(v.clean, room.bed == 2); SetActive(v.dirty, room.bed == 1); v.bed = room.bed;
            }
            SetActive(v.towel, room.towel); SetActive(v.trash, room.trash || (mvp && room.mvp.binFill > .1f));
            MvpEquipmentState sink = Equipment(room, "sink");
            SetActive(v.leak, room.leak && !waterFault && (!mvp || (sink != null && sink.installed)));
            SetActive(v.upgraded, bedQuality >= 2);
            float water = Mathf.Clamp01(room.water);
            if (Mathf.Abs(water - v.waterAmount) > .001f)
            {
                SetActive(v.water.gameObject, water > .001f);
                float size = Mathf.Lerp(.36f, 1.4f, Mathf.Sqrt(water));
                v.water.localScale = new Vector3(size, 1, size);
                v.waterAmount = water;
            }
            bool dirty = room.bed != 2 || room.trash || !room.towel || room.water > .001f || dirtAmount > .001f || dirtyTowels > 0;
            int flag = room.outOfService ? 4 : room.leak ? 3 : powerFault ? 6 : waterFault ? 7 : equipmentFault ? 5 : room.guestId != 0 ? 2 : dirty ? 1 : 0;
            if (flag != v.flag)
            {
                v.status.text = flag == 7 ? "НЕТ ВОДЫ" : flag == 6 ? "НЕТ СВЕТА" : flag == 5 ? "РЕМОНТ" : flag == 4 ? "ЗАКРЫТ" : flag == 3 ? "ТЕЧЬ!" : flag == 2 ? "ЗАНЯТ" : flag == 1 ? "УБОРКА" : "ГОТОВ";
                Ink color = flag >= 3 ? Ink.Red : flag == 1 ? Ink.Gold : flag == 2 ? Ink.Blue : Ink.Green;
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
        public static GameObject MakeItem(string kind) => MakeItem(kind, "small", "clean");

        /// <summary>Use this overload for first-person carry to preserve authoritative size/condition.</summary>
        public static GameObject MakeItem(string kind, string size, string condition)
        {
            EnsureAssets();
            Transform p = Group(null, "Item / " + kind);
            switch (kind)
            {
                case "plunger":
                    Shape(p, "Plunger rubber cup", cone, new Vector3(0, .115f, 0), new Vector3(.32f, .18f, .32f), Ink.Burgundy);
                    Cylinder(p, "Plunger suction rim", new Vector3(0, .03f, 0), new Vector3(.35f, .06f, .35f), Ink.Dark);
                    Tube(p, "Plunger wooden handle", new Vector3(0, .18f, 0), new Vector3(0, 1.04f, 0), .045f, Ink.Oak);
                    Ball(p, "Plunger grip", new Vector3(0, 1.05f, 0), new Vector3(.071f, .16f, .071f), Ink.Wood);
                    break;
                case "coffee":
                case "coffeecup":
                    Cylinder(p, "Coffee saucer", new Vector3(0, .016f, 0), new Vector3(.27f, .03f, .27f), Ink.Ceramic);
                    Cup(p, new Vector3(0, .032f, 0));
                    Box(p, "Coffee service napkin", new Vector3(.025f, .01f, .035f), new Vector3(.28f, .012f, .23f), Ink.Paper);
                    break;
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
                case "dirtytowel":
                case "dirtylinen":
                    bool towel = kind == "towel" || kind == "dirtytowel", dirty = kind == "dirtylinen" || kind == "dirtytowel" || condition == "dirty";
                    int layers = towel ? 2 : 3;
                    for (int j = 0; j < layers; ++j)
                    {
                        GameObject fold = Rounded(p, "Folded textile", new Vector3(j % 2 * .012f, .055f + j * .081f, 0), new Vector3(towel ? .43f : .65f, .104f, towel ? .33f : .43f), dirty ? Ink.Dirty : Ink.White);
                        if (dirty) fold.transform.localRotation = Quaternion.Euler(0, j * 9 - 8, j * 3);
                        Box(p, "Woven edge", new Vector3(0, .06f + j * .081f, towel ? -.17f : -.22f), new Vector3(towel ? .38f : .6f, .02f, .011f), dirty ? Ink.Wood : Ink.Teal);
                    }
                    if (dirty) Ball(p, "Laundry stain", new Vector3(.12f, towel ? .2f : .28f, -.03f), new Vector3(.2f, .02f, .13f), Ink.Wood);
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
            string itemLabel = condition == "dirty" && (kind == "towel" || kind == "linen") ? (kind == "towel" ? "Грязное полотенце → корзина" : "Грязное бельё → корзина") : ItemLabel(kind);
            Target(p.gameObject, kind ?? "bag", itemLabel);
            if (kind == "bag" && size == "large") p.localScale = new Vector3(1.3f, 1.35f, 1.25f);
            return p.gameObject;
        }

        /// <summary>Seeded, recognizable humanoid with pivot at its feet, facing local +Z.</summary>
        public static GameObject MakePerson(bool employee, int seed)
        {
            EnsureAssets();
            Transform p = Group(null, employee ? "Hotel employee" : "Hotel guest");
            HotelFigure rig = p.gameObject.AddComponent<HotelFigure>();
            rig.employee = employee;
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
                Transform brow = Rounded(rig.head, "Raised eyebrow", new Vector3(side * .089f, .224f, .179f), new Vector3(.104f, .028f, .04f), Ink.Hair).transform;
                brow.localRotation = Quaternion.Euler(0, 0, side * (variation % 2 == 0 ? -8 : 11));
                if (side < 0) rig.leftBrow = brow; else rig.rightBrow = brow;
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
            rig.CaptureExpression();
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

        static string GuestCaption(GuestState guest, string request = null)
        {
            string name = string.IsNullOrEmpty(guest.name) ? "Гость " + guest.id : guest.name;
            if (name.Length > 17) name = name.Substring(0, 16) + "…";
            if (guest.mvp != null && guest.mvp.partySize > 1) name += " ×" + guest.mvp.partySize;
            string line = guest.stage == "queue" ? "ЖДЁТ НОМЕР" : guest.stage == "checkout" ? "ВЫЕЗД" :
                guest.stage == "leaving" ? "ДО СВИДАНИЯ" : request == "coffee" ? "ЖДЁТ КОФЕ" : request == "cleaning" ? "НУЖНА УБОРКА" : guest.towelRequested || request == "towel" ? "НУЖНО ПОЛОТЕНЦЕ" :
                !guest.luggageDelivered || request == "luggage" ? "ЖДЁТ БАГАЖ" : guest.satisfaction < 45 ? "НЕДОВОЛЕН" : "НОМЕР " + guest.room;
            return name + "\n" + line;
        }

        static string ItemLabel(string kind)
        {
            switch (kind)
            {
                case "toolbox": return "Ящик инструментов";
                case "mop": return "Швабра";
                case "plunger": return "Вантуз";
                case "coffee": case "coffeecup": return "Кофе для гостя";
                case "dirtytowel": return "Грязное полотенце → корзина";
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
            for (int n = HotelLayout.FirstRoom; n <= HotelLayout.LastRoom; ++n)
            {
                rooms[n].roomLight = PointLight(p, HotelLayout.RoomCenter(n) + new Vector3(0, 2.8f, -.2f), 5.9f, 3.2f, true);
                rooms[n].roomLight.enabled = rooms[n].owned;
            }
            foreach (float z in new[] { 3.1f, 8.6f, 14.3f, 20.7f })
            {
                Lamp(architecture, new Vector3(0, 3.12f, z), .36f);
                PointLight(p, new Vector3(0, 2.8f, z), 4.7f, 2.4f, false);
            }
        }

        static Light PointLight(Transform parent, Vector3 position, float range, float intensity, bool shadows)
        {
            Light light = Group(parent, "Warm pool of light", position).gameObject.AddComponent<Light>();
            light.type = LightType.Point; light.range = range; light.intensity = intensity;
            light.color = new Color(1, .86f, .69f); light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            light.shadowStrength = .7f; light.shadowBias = .045f; light.shadowNormalBias = .22f;
            light.shadowCustomResolution = 512;
            return light;
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

        static Transform Lamp(Transform p, Vector3 position, float width)
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
            return lamp;
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
        internal Transform leftBrow, rightBrow;
        internal float phase;
        internal bool employee;
        Vector3 desired;
        float desiredYaw, pitch, stride;
        bool initialized, carrying, working;
        Camera facingCamera;
        // Waiting, request and frustration are presentation-only weights. They never alter DTOs,
        // navigation, the root capsule, or the existing walk cycle. No coroutine or clip is created.
        Vector3 reaction, wantedReaction;
        Vector3 leftBrowPosition, rightBrowPosition, mouthScale;
        Quaternion leftBrowRotation, rightBrowRotation;

        internal void CaptureExpression()
        {
            leftBrowPosition = leftBrow.localPosition; rightBrowPosition = rightBrow.localPosition;
            leftBrowRotation = leftBrow.localRotation; rightBrowRotation = rightBrow.localRotation;
            mouthScale = mouth.localScale;
        }

        internal void React(string stage, float satisfaction, bool towelRequested)
        {
            bool waiting = stage == "queue" || stage == "checkout";
            bool staying = stage == "staying";
            bool visibleStage = waiting || staying || stage == "walking" || stage == "leaving";
            float frustration = visibleStage && satisfaction < 45 ? .65f + .35f * Mathf.Clamp01((45 - satisfaction) / 45) : 0;
            // A stale towel flag must not ask for room service after checkout or while walking.
            wantedReaction = employee ? Vector3.zero : new Vector3(waiting ? 1 : 0, staying && towelRequested ? 1 : 0, frustration);
        }

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
            Animate(Time.deltaTime, Time.time);
            if (!initialized) return;
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

        // Explicit clock lets the native tests exercise the same path without entering play mode.
        internal void Animate(float deltaTime, float time)
        {
            if (!initialized) return;
            float dt = Mathf.Clamp(deltaTime, 0, .1f);
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
            reaction = Vector3.Lerp(reaction, wantedReaction, 1 - Mathf.Exp(-7 * dt));
            float freeArms = carrying || working ? 0 : 1 - Mathf.Clamp01(stride * 3);
            float waiting = reaction.x * (1 - .65f * reaction.z) * freeArms;
            float request = reaction.y * freeArms;
            float complaint = reaction.z * (1 - reaction.y) * freeArms;
            float gesture = Mathf.Sin(time * 2.2f + phase);
            Quaternion leftPose = Quaternion.Euler(carrying ? -52 : -swing * .7f, 0, 7);
            Quaternion rightPose = Quaternion.Euler(working ? -65 + Mathf.Sin(time * 12) * 15 : carrying ? -63 : swing * .7f, 0, -7);
            // Queue/checkout: hands together at the waist and a restrained searching glance.
            leftPose = Quaternion.Slerp(leftPose, Quaternion.Euler(-32, 20, 12), waiting);
            rightPose = Quaternion.Slerp(rightPose, Quaternion.Euler(-32, -20, -12), waiting);
            // Dissatisfied: one open hand at chest level; a request still keeps its raised hand.
            leftPose = Quaternion.Slerp(leftPose, Quaternion.Euler(-12, 12, -15), complaint);
            rightPose = Quaternion.Slerp(rightPose, Quaternion.Euler(-54 + gesture * 3, -18, -23), complaint);
            leftArm.localRotation = Quaternion.Slerp(leftPose, Quaternion.Euler(-14, 0, 10), request);
            rightArm.localRotation = Quaternion.Slerp(rightPose, Quaternion.Euler(-132 + gesture * 4, -10, -20 + gesture * 6), request);
            body.localPosition = new Vector3(Mathf.Sin(time * .7f + phase) * .015f * waiting,
                Mathf.Abs(Mathf.Sin(phase)) * .026f * stride + Mathf.Sin(time * 1.8f + phase) * .004f, 0);
            head.localRotation = Quaternion.Euler(pitch * .5f + reaction.z * 5 - request * (3 + gesture * 2),
                Mathf.Sin(time * .8f + phase) * (stride < .1f ? 5 : 0) + Mathf.Sin(time * .65f + phase) * 8 * waiting,
                reaction.z * 6 - request * 5);
            // Reapply from captured rest values: long sessions cannot accumulate transform drift.
            float browRaise = reaction.y * (1 - reaction.z) * .025f;
            leftBrow.localPosition = leftBrowPosition + Vector3.up * browRaise;
            rightBrow.localPosition = rightBrowPosition + Vector3.up * browRaise;
            leftBrow.localRotation = Quaternion.Slerp(leftBrowRotation, Quaternion.Euler(0, 0, -24), reaction.z);
            rightBrow.localRotation = Quaternion.Slerp(rightBrowRotation, Quaternion.Euler(0, 0, 24), reaction.z);
            mouth.localRotation = Quaternion.Euler(0, 0, -16 * reaction.z);
            mouth.localScale = Vector3.Scale(mouthScale, new Vector3(1 - .35f * reaction.y, 1 + .7f * reaction.y, 1));
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
