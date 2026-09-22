using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        private HotelMvpGuestCatalog hospitalityCatalog;
        private HotelMvpGuestCatalog HospitalityCatalog
        { get { return hospitalityCatalog ?? (hospitalityCatalog = HotelMvpGuestCatalog.Load()); } }

        private bool TryHospitalityCommand(PlayerState p, HotelCommand c, out string error)
        {
            error = "";
            if (State.mvp == null) return false;
            if (c.action == "price")
            {
                if (p.id != 0) error = "Цену меняет хозяин отеля.";
                else if (!Near(p, HotelLayout.Target("board"))) error = "Подойдите к доске управления.";
                else if (State.phase != "preparation") error = "Цену меняют во время подготовки.";
                else if (c.target != "low" && c.target != "normal" && c.target != "high") error = "Неизвестный режим цены.";
                else
                {
                    State.mvp.priceMode = c.target;
                    ApplyHospitalityOfferPrice();
                    State.notice = "Цена изменила тариф и спрос будущих walk-in. Подтверждённые брони и проживающие гости сохраняют договорённости.";
                }
                return true;
            }
            if (c.action != "checkin" && c.action != "checkout" && c.action != "compensate" && c.action != "relocate" && c.action != "refuse") return false;
            if (!Near(p, HotelLayout.Target("desk"))) { error = "Подойдите к стойке регистрации."; return true; }
            int id = c.guestId != 0 ? c.guestId : c.number;
            switch (c.action)
            {
                case "checkin":
                    GuestState selected = c.guestId != 0 ? Guest(c.guestId) : State.guests.Find(g => g.stage == "queue");
                    error = HospitalityCheckIn(selected, c.number); break;
                case "checkout": error = HospitalityCheckOut(Guest(id)); break;
                case "compensate": error = HospitalityCompensate(Guest(id)); break;
                case "relocate": error = HospitalityRelocate(Guest(c.guestId), c.number); break;
                case "refuse": error = HospitalityRefuse(c.guestId != 0 ? c.guestId : c.number, c.target); break;
            }
            return true;
        }

        private string HospitalityCheckIn(GuestState g, int number)
        {
            string reason = HotelHospitalityRules.AssignmentBlockReason(State, g == null ? 0 : g.id, number);
            if (reason != "") return reason;
            MvpReservationState booking = ReservationFor(g);
            if (!string.IsNullOrEmpty(g.mvp.reservationId) && (booking == null || !HotelHospitalityRules.Committed(booking)))
                return "Бронь гостя больше не действует.";
            if (booking == null && State.mvp.reservations.Count >= HotelHospitalityRules.MaxReservations) return "Список бронирований заполнен.";
            // All checks precede IDs, links, payment or item mutations.
            if (booking == null)
            {
                string bookingId = MvpId("reservation");
                g.mvp.reservationId = bookingId;
                booking = new MvpReservationState { id = bookingId, source = "walkin", partyId = g.mvp.partyId,
                    groupId = g.mvp.groupId, arrivalDay = g.mvp.arrivalDay, departureDay = g.mvp.departureDay,
                    arrivalTime = State.time, guestId = g.id, contract = HotelMvpGuestCatalog.Copy(g.mvp) };
                State.mvp.reservations.Add(booking);
                MvpGroupState group = GroupFor(g.mvp.groupId);
                if (group != null) group.reservationIds.Add(bookingId);
            }
            booking.room = number; booking.status = "staying"; booking.guestId = g.id;
            Room(number).guestId = g.id; g.room = number; g.stage = "walking";
            g.waited = 0; g.mvp.blockedTime = 0;
            MvpGroupState admittedGroup = GroupFor(g.mvp.groupId);
            if (admittedGroup != null) admittedGroup.admitted++;
            IssueRequest(g, "luggage");
            HotelOnboarding.Record(State, HotelTutorialSkill.CheckIn);
            State.notice = g.name + " заселён в " + number + (g.mvp.partySize > 1 ? " · 2 человека." : ".");
            RefreshRequestFulfillment();
            UpdateHospitalityGroups();
            return "";
        }

        private string HospitalityCheckOut(GuestState g)
        {
            if (g == null || g.mvp == null) return "Гость не найден.";
            if (g.paid) return "Проживание уже оплачено.";
            if (g.stage != "checkout") return "Гость пока не готов к выезду.";
            if (!Finite(g.position) || FlatDistance(g.position, Reception) > .15f) return "Дождитесь гостя у стойки.";
            HospitalitySettle(g);
            HotelOnboarding.Record(State, HotelTutorialSkill.CheckOut);
            return "";
        }

        private string HospitalityCompensate(GuestState g)
        {
            if (g == null || g.mvp == null || (g.stage != "staying" && g.stage != "checkout")) return "Компенсация доступна проживающему гостю.";
            if (g.compensated) return "Гостю уже выдали компенсацию.";
            if (State.cash < 25) return "Для компенсации нужно 25.";
            g.compensated = true; State.cash -= 25; State.expenses += 25;
            HospitalityMemory(g, "Персонал предложил компенсацию", g.mvp.trait == "friendly" ? 20 : 15);
            Log("Компенсация #" + g.id + ": −25");
            return ""; // Physical problems and their complaints remain active.
        }

        private string HospitalityRelocate(GuestState g, int destination)
        {
            if (State.phase != "open" && State.phase != "closing") return "Переселение доступно во время смены.";
            if (g == null || g.mvp == null || g.paid || (g.stage != "staying" && g.stage != "walking")) return "Выберите проживающего гостя.";
            string reason = HotelHospitalityRules.RoomForGuestBlockReason(State, g, destination, true);
            if (reason != "") return reason;
            MvpReservationState booking = ReservationFor(g);
            if (booking == null || !HotelHospitalityRules.Committed(booking)) return "Не найдена действующая бронь проживания.";
            RoomState previous = Room(g.room), next = Room(destination);
            if (previous == null || previous.guestId != g.id) return "Нарушена связь гостя с номером.";
            previous.guestId = 0; previous.bed = 1;
            next.guestId = g.id; booking.room = destination;
            g.room = destination; g.stage = "walking"; g.mvp.blockedTime = 0;
            // Position is deliberately retained. The route first exits the physical old room.
            foreach (MvpRequestState r in State.mvp.requests)
                if (r.guestId == g.id && r.status == "open") r.target = RequestTarget(r.kind, destination);
            RefreshRequestFulfillment();
            State.notice = g.name + " переселяется в " + destination + ". Перенесите его багаж.";
            return "";
        }

        private string HospitalityRefuse(int id, string reservationId)
        {
            if (!string.IsNullOrEmpty(reservationId))
            {
                MvpReservationState booking = State.mvp.reservations.Find(r => r.id == reservationId);
                if (booking == null || !HotelHospitalityRules.Committed(booking)) return "Действующая бронь не найдена.";
                GuestState arrived = booking.guestId == 0 ? null : Guest(booking.guestId);
                if (arrived != null)
                {
                    if (arrived.stage != "queue") return "Нельзя отменить уже начавшееся проживание.";
                    RefuseHospitalityGuest(arrived); return "";
                }
                if (booking.status != "confirmed") return "Бронь уже обрабатывается.";
                CancelHospitalityBooking(booking, "refused");
                UpdateHospitalityGroups();
                return "";
            }
            GuestState g = Guest(id);
            if (g == null || g.mvp == null || g.stage != "queue") return "Отказать можно ожидающему гостю.";
            RefuseHospitalityGuest(g);
            return "";
        }

        private void HospitalityStep(float dt)
        {
            if (State.mvp == null || (State.phase != "open" && State.phase != "closing")) return;
            bool held = HotelDirector.ClockHeld(State);
            PruneHospitality();
            if (State.phase == "open") SpawnHospitalityArrivals(held);
            UpdateHospitalityNoise(held);
            // Arrivals are complete before enumeration. No guest is appended inside this loop.
            foreach (GuestState g in State.guests)
            {
                if (g.mvp == null) continue;
                switch (g.stage)
                {
                    case "queue":
                        if (!held) g.waited += dt;
                        if (!held && (g.waited >= g.mvp.patience || State.phase == "closing")) RefuseHospitalityGuest(g);
                        break;
                    case "walking":
                        if (HospitalityMove(g, true, dt))
                        {
                            g.stage = "staying";
                            RoomState r = Room(g.room);
                            if (!held && r.mvp.bedQuality < g.mvp.preferredBedQuality)
                                HospitalityMemoryOnce(g, "Ожидал более удобную кровать", -8);
                            if (!held && (r.mvp.tvQuality < g.mvp.preferredTvQuality || (g.mvp.preferredTvQuality > 0 && !HotelHospitalityRules.Effective(State, r, "tv"))))
                                HospitalityMemoryOnce(g, "TV ниже ожиданий", -6);
                        }
                        if (!held && DepartureDue(g)) StartHospitalityCheckout(g);
                        break;
                    case "staying":
                        if (!held) { g.stay += dt; g.mvp.serviceElapsed += dt; }
                        else if (g.id == State.guidedGuestId && g.luggageDelivered) g.mvp.serviceElapsed += dt;
                        g.requestElapsed = g.mvp.serviceElapsed;
                        bool introductory = held && g.id == State.guidedGuestId && g.luggageDelivered;
                        if (!g.mvp.requestIssued && g.mvp.serviceElapsed >= g.mvp.requestDelay &&
                            (introductory || (!held && State.mvp.director.band != "overload")))
                        {
                            string kind = introductory ? "towel" : g.mvp.preferredRequest;
                            if (kind != "cleaning" || !HotelHospitalityRules.CleanForRequest(Room(g.room)))
                            { IssueRequest(g, kind); g.mvp.requestIssued = true; }
                        }
                        if (!held && DepartureDue(g)) StartHospitalityCheckout(g);
                        break;
                    case "checkout":
                        if (HospitalityMove(g, false, dt) && !held)
                        {
                            g.waited += dt;
                            if (g.waited >= Mathf.Clamp(g.mvp.patience * .45f, 60, 180)) HospitalitySettle(g);
                        }
                        break;
                    case "leaving":
                        g.position = Vector3.MoveTowards(g.position, Exit, dt * 1.6f);
                        if (FlatDistance(g.position, Exit) < .03f) g.stage = "gone";
                        break;
                }
                if (!held) ReconcileHospitalityComplaints(g, dt);
            }
            UpdateHospitalityNoise(held);
            UpdateHospitalityGroups();
        }

        private void UpdateHospitalityNoise(bool paused)
        {
            foreach (RoomState room in State.rooms)
            {
                if (room.mvp == null) continue;
                GuestState guest = Guest(room.guestId);
                if (paused || !room.mvp.owned || guest == null || guest.mvp == null || guest.stage != "staying")
                { room.mvp.noise = 0; continue; }
                // Short visible activity bursts have a persisted phase (serviceElapsed).
                // Families and messy guests are louder; later rest hours make it more intrusive.
                bool activity = guest.mvp.serviceElapsed % 180 < 55;
                float volume = guest.mvp.partySize > 1 ? .75f : guest.mvp.trait == "messy" ? .7f : .2f;
                if (guest.mvp.trait == "messy") volume += .15f;
                if (State.time >= State.dayLength * .55f) volume += .1f;
                room.mvp.noise = Mathf.Clamp01(activity ? volume : volume * .2f);
            }
        }

        private bool DepartureDue(GuestState g)
        { return State.day >= g.mvp.departureDay || (State.day == g.mvp.departureDay - 1 && State.time >= State.dayLength * .75f); }
        private void StartHospitalityCheckout(GuestState g)
        {
            if (g.stage == "checkout" || g.paid) return;
            g.stage = "checkout"; g.waited = 0; g.mvp.blockedTime = 0;
            State.notice = g.name + " идёт к стойке для выезда.";
        }

        // Axis-aligned routes use actual position, including when a relocated guest is still
        // in the old room. Brief yielding in shared space always expires, never hard-blocks.
        private bool HospitalityMove(GuestState g, bool inbound, float dt)
        {
            bool shared = Math.Abs(g.position.x) < 1.7f || g.position.z < 2;
            bool blocked = shared && State.guests.Exists(other => other.id < g.id && other.id != g.id &&
                other.stage != "gone" && other.stage != "leaving" && FlatDistance(other.position, g.position) < .4f);
            if (blocked)
            {
                g.mvp.blockedTime += dt;
                if (g.mvp.blockedTime < 2) return false;
            }
            else g.mvp.blockedTime = 0;
            float distance = dt * 1.6f;
            Vector3 destination = inbound ? HotelLayout.RoomCenter(g.room) : Reception;
            for (int i = 0; i < 10 && distance > .00001f; i++)
            {
                Vector3 p = g.position, target;
                Vector3 center = HotelLayout.RoomCenter(g.room);
                bool atDestinationRow = Math.Abs(p.z - center.z) < .001f;
                bool destinationSide = Math.Sign(p.x) == Math.Sign(center.x);
                if (p.z > 2.001f && Math.Abs(p.x) > 1.49f)
                {
                    if (inbound && atDestinationRow && destinationSide) target = center;
                    else
                    {
                        int row = Mathf.Clamp(Mathf.RoundToInt((p.z - 5.5f) / 7), 0, 2);
                        float z = 5.5f + row * 7;
                        target = Math.Abs(p.z - z) > .001f ? new Vector3(p.x, 0, z) : new Vector3(0, 0, z);
                    }
                }
                else if (p.z > 2.001f && Math.Abs(p.x) > .001f && (!inbound || !atDestinationRow || !destinationSide))
                    target = new Vector3(0, 0, p.z);
                else if (inbound)
                {
                    if (p.z < -2.001f) target = new Vector3(1.85f, 0, -2);
                    else if (p.z < .999f) target = new Vector3(1.85f, 0, 1);
                    else if (p.z < 1.999f) target = new Vector3(0, 0, 2);
                    else if (!atDestinationRow) target = new Vector3(0, 0, center.z);
                    else target = center;
                }
                else
                {
                    if (p.z > 2.001f) target = new Vector3(0, 0, 2);
                    else if (p.z > 1.001f) target = new Vector3(1.85f, 0, 1);
                    else if (p.z > -1.999f) target = new Vector3(1.85f, 0, -2);
                    else target = Reception;
                }
                float length = Vector3.Distance(p, target);
                g.position = Vector3.MoveTowards(p, target, distance);
                distance -= length;
                if (FlatDistance(g.position, destination) < .001f) return true;
                if (length < .000001f) break;
            }
            return false;
        }

        private void PrepareDemand()
        {
            if (State.mvp == null || State.phase != "preparation" || State.mvp.preparedDay == State.day) return;
            PruneHospitality();
            bool guide = State.day == 1 && State.guidedOpening && State.guidedStage < HotelDirector.Released;
            if (guide && !State.mvp.schedule.Exists(x => x.day == State.day && x.status == "planned"))
            {
                MvpGuestState first = HospitalityCatalog.Snapshot("tourist", "patient", State.mvp.priceMode, State.day, 1);
                first.preferredRequest = "towel"; first.preferredBedQuality = 1; first.preferredTvQuality = 0;
                AddHospitalitySchedule(first, "walkin", "", 0, null);
            }
            int rooms = State.rooms.FindAll(r => r.mvp != null && r.mvp.owned && !r.outOfService).Count;
            int planned = State.mvp.schedule.FindAll(x => x.day == State.day && x.status == "planned").Count;
            // Save the prospective LOW-price pool once. Later price changes select a prefix
            // of these same candidates, without new IDs, traits, arrival times or RNG draws.
            int target = Mathf.Clamp(Mathf.CeilToInt(rooms * (.45f + State.mvp.reputation * .12f) * 1.2f) + MvpRandom(2), 0, 8);
            if (guide) target = Math.Min(target, 3);
            for (int i = planned; i < target; i++)
            {
                if (State.mvp.schedule.Count >= HotelHospitalityRules.MaxSchedule) break;
                string archetype = ChooseDemandArchetype();
                MvpGuestState c = NewHospitalityContract(archetype, State.day, guide ? 1 : 1 + (MvpRandom(4) == 0 ? 1 : 0));
                if (FindBookingRoom(c, null) == 0) continue;
                float time = Mathf.Min(State.dayLength * .6f, 80 + i * 115 + MvpRandom(45));
                AddHospitalitySchedule(c, "walkin", "", time, null);
            }
            // Book a small next-day base separately from today's unreserved walk-ins.
            if (!guide)
            {
                if (State.day % 3 == 0) { string ignored; TryReserveBatch(Math.Min(3, Math.Max(2, rooms / 2)), "tourist_group", out ignored); }
                int count = Math.Min(2, Math.Max(0, rooms / 2));
                for (int i = 0; i < count; i++)
                {
                    MvpGuestState c = NewHospitalityContract(ChooseDemandArchetype(), State.day + 1, 1 + (MvpRandom(3) == 0 ? 1 : 0));
                    int room = FindBookingRoom(c, null);
                    if (room != 0 && State.mvp.reservations.Count < HotelHospitalityRules.MaxReservations && State.mvp.schedule.Count < HotelHospitalityRules.MaxSchedule)
                        CommitHospitalityBooking(c, room, "booking", "", 45 + i * 100);
                }
            }
            ApplyHospitalityOfferPrice();
            State.mvp.preparedDay = State.day;
        }

        private void ApplyHospitalityOfferPrice()
        {
            // A cancelled, never-arrived, unreserved current-day walk-in is a suppressed
            // candidate while in preparation. Finish also cancels missed arrivals, but they
            // can never be revived: price is preparation-only and filters the new current day.
            var offers = State.mvp.schedule.FindAll(s => s.day == State.day && s.source == "walkin" &&
                s.guestId == 0 && string.IsNullOrEmpty(s.reservationId) && (s.status == "planned" || s.status == "cancelled"));
            offers.Sort((a, b) => { int time = a.time.CompareTo(b.time); return time != 0 ? time : string.CompareOrdinal(a.id, b.id); });
            float fraction = State.mvp.priceMode == "low" ? 1 : State.mvp.priceMode == "high" ? .5f : .8f;
            int admitted = offers.Count == 0 ? 0 : Math.Max(1, Mathf.FloorToInt(offers.Count * fraction));
            for (int i = 0; i < offers.Count; i++)
            {
                MvpScheduleState offer = offers[i];
                MvpGuestState quote = HospitalityCatalog.Snapshot(offer.contract.archetype, offer.contract.trait,
                    State.mvp.priceMode, offer.contract.arrivalDay, offer.contract.billableNights);
                quote.partyId = offer.contract.partyId; quote.groupId = offer.contract.groupId;
                if (State.day == 1 && State.guidedOpening && offer.time == 0)
                { quote.preferredBedQuality = 1; quote.preferredTvQuality = 0; }
                offer.contract = quote;
                offer.status = i < admitted ? "planned" : "cancelled";
            }
        }

        private string ChooseDemandArchetype()
        {
            int choice = MvpRandom(100);
            if (choice >= 88 && State.mvp.reputation >= 3.5f) return "vip";
            if (choice >= 60) return "family";
            if (choice >= 30) return "business";
            return "tourist";
        }
        private MvpGuestState NewHospitalityContract(string archetype, int day, int nights)
        { return HospitalityCatalog.Snapshot(archetype, HotelMvpGuestCatalog.TraitIds[MvpRandom(5)], State.mvp.priceMode, day, nights); }

        private bool TryScheduleArrival(string archetype, string source, string groupId = "")
        {
            if (State.mvp == null || State.phase != "open" || HotelDirector.ClockHeld(State) || State.time > State.dayLength * .65f ||
                Array.IndexOf(HotelMvpGuestCatalog.ArchetypeIds, archetype) < 0 || string.IsNullOrEmpty(source) || source.Length > 40 ||
                (archetype == "vip" && State.mvp.reputation < 3.5f) || State.mvp.schedule.Count >= HotelHospitalityRules.MaxSchedule ||
                (!string.IsNullOrEmpty(groupId) && GroupFor(groupId) == null)) return false;
            int pending = State.mvp.schedule.FindAll(x => x.day == State.day && x.status == "planned").Count;
            int present = State.guests.FindAll(g => g.stage != "gone" && g.stage != "leaving").Count;
            if (pending + present >= HotelHospitalityRules.MaxGuests) return false;
            // Probe without consuming RNG. A failed event must not alter the world or reroll later decisions.
            MvpGuestState probe = HospitalityCatalog.Snapshot(archetype, "patient", State.mvp.priceMode, State.day, 1);
            if (FindBookingRoom(probe, null) == 0) return false;
            MvpGuestState c = NewHospitalityContract(archetype, State.day, 1);
            AddHospitalitySchedule(c, source, groupId, Math.Min(State.dayLength * .7f, State.time + 8), null);
            return true;
        }

        private bool TryReserveBatch(int count, string source, out string groupId)
        {
            groupId = "";
            if (State.mvp == null || count < 2 || count > 6 || string.IsNullOrEmpty(source) || source.Length > 40 ||
                State.mvp.groups.Count >= HotelHospitalityRules.MaxGroups || State.mvp.schedule.Count + count > HotelHospitalityRules.MaxSchedule) return false;
            bool walkin = source == "walkin_group";
            if (walkin && (State.phase != "open" || HotelDirector.ClockHeld(State) || State.time > State.dayLength * .65f)) return false;
            if (!walkin && (State.phase != "preparation" || State.mvp.reservations.Count + count > HotelHospitalityRules.MaxReservations)) return false;
            int day = walkin ? State.day : State.day + 1;
            int pending = State.mvp.schedule.FindAll(x => x.day == day && x.status == "planned").Count;
            if (walkin && pending + State.guests.FindAll(g => g.stage != "gone" && g.stage != "leaving").Count + count > HotelHospitalityRules.MaxGuests) return false;
            var rooms = new List<int>();
            MvpGuestState probe = HospitalityCatalog.Snapshot("tourist", "patient", State.mvp.priceMode, day, 1);
            for (int i = 0; i < count; i++)
            {
                int room = FindBookingRoom(probe, rooms);
                if (room == 0) return false;
                rooms.Add(room);
            }
            // Allocation succeeded for every party. Only now consume IDs/RNG and publish records.
            groupId = MvpId("group");
            State.mvp.groups.Add(new MvpGroupState { id = groupId, source = walkin ? "walkin_group" : "planned", arrivalDay = day });
            for (int i = 0; i < count; i++)
            {
                MvpGuestState c = NewHospitalityContract("tourist", day, 1);
                if (walkin) AddHospitalitySchedule(c, source, groupId, State.time + 8 + i * 3, null);
                else CommitHospitalityBooking(c, rooms[i], source, groupId, 65 + i * 5);
            }
            return true;
        }

        private int FindBookingRoom(MvpGuestState contract, List<int> excluded)
        {
            foreach (RoomState room in State.rooms)
                if ((excluded == null || !excluded.Contains(room.number)) &&
                    HotelHospitalityRules.BookingBlockReason(State, contract, room.number) == "") return room.number;
            return 0;
        }
        private void CommitHospitalityBooking(MvpGuestState c, int room, string source, string groupId, float time)
        {
            c.reservationId = MvpId("reservation"); c.partyId = MvpId("party"); c.groupId = groupId;
            var r = new MvpReservationState { id = c.reservationId, room = room, source = source, groupId = groupId,
                partyId = c.partyId, arrivalDay = c.arrivalDay, departureDay = c.departureDay,
                arrivalTime = time, contract = HotelMvpGuestCatalog.Copy(c) };
            State.mvp.reservations.Add(r);
            MvpGroupState group = GroupFor(groupId);
            if (group != null) group.reservationIds.Add(r.id);
            AddHospitalitySchedule(c, source, groupId, time, r);
        }
        private void AddHospitalitySchedule(MvpGuestState c, string source, string groupId, float time, MvpReservationState reservation)
        {
            if (string.IsNullOrEmpty(c.partyId)) c.partyId = MvpId("party");
            c.groupId = groupId;
            State.mvp.schedule.Add(new MvpScheduleState { id = MvpId("arrival"), day = c.arrivalDay, time = time,
                source = source, groupId = groupId, reservationId = reservation == null ? "" : reservation.id,
                contract = HotelMvpGuestCatalog.Copy(c) });
        }

        private void SpawnHospitalityArrivals(bool held)
        {
            foreach (MvpScheduleState entry in State.mvp.schedule)
            {
                if (entry.status != "planned" || entry.day != State.day || State.guests.Count >= HotelHospitalityRules.MaxGuests) continue;
                if (held)
                {
                    if (State.guests.Exists(g => !g.paid && (g.stage == "queue" || g.stage == "walking" || g.stage == "staying"))) break;
                }
                else if (entry.time > State.time) continue;
                MvpReservationState booking = string.IsNullOrEmpty(entry.reservationId) ? null : State.mvp.reservations.Find(r => r.id == entry.reservationId);
                if (!string.IsNullOrEmpty(entry.reservationId) && (booking == null || booking.status != "confirmed"))
                { entry.status = "cancelled"; continue; }
                int id = State.nextGuest++;
                string[] names = { "Анна", "Борис", "Вера", "Григорий", "Дарья", "Егор", "Жанна", "Илья", "Кира", "Лев", "Мария", "Никита" };
                MvpGuestState contract = HotelMvpGuestCatalog.Copy(entry.contract);
                var guest = new GuestState { id = id, name = names[(id - 1) % names.Length], kind = contract.archetype,
                    trait = HotelMvpGuestCatalog.TraitName(contract.trait), mvp = contract,
                    position = new Vector3(-.8f + (State.arrivals % 3) * .55f, 0, -3.3f - ((State.arrivals / 3) % 4) * .55f) };
                State.guests.Add(guest); State.arrivals++;
                State.items.Add(new ItemState { id = "luggage_" + id, kind = "bag", ownerGuest = id,
                    size = contract.partySize > 1 || contract.archetype == "vip" ? "large" : "small",
                    position = new Vector3(2.6f + (id % 4) * .55f, .25f, -4.4f - ((id / 4) % 3) * .5f) });
                entry.status = "arrived"; entry.guestId = id;
                if (booking != null) { booking.status = "arrived"; booking.guestId = id; }
                HotelDirector.Arrived(State, guest);
                State.notice = guest.name + " · " + HotelMvpGuestCatalog.ArchetypeName(contract.archetype) + " ждёт регистрации.";
                if (held) break;
            }
        }

        private void IssueRequest(GuestState g, string kind)
        {
            if (State.mvp.requests.Exists(r => r.guestId == g.id && r.kind == kind) || State.mvp.requests.Count >= 128) return;
            State.mvp.requests.Add(new MvpRequestState { id = MvpId("request"), kind = kind, guestId = g.id,
                target = RequestTarget(kind, g.room), createdAt = State.mvp.elapsed, dueAt = State.mvp.elapsed + g.mvp.requestGrace });
            if (kind == "towel")
            {
                g.towelRequested = true;
                HospitalityMemoryOnce(g, "Попросил дополнительное полотенце", 0);
            }
            State.notice = g.name + ": " + HotelHospitalityRules.RequestName(kind) + " в номер " + g.room + ".";
        }
        private static string RequestTarget(string kind, int number)
        { return (kind == "luggage" ? "bag" : kind == "cleaning" ? "clean" : kind) + "_" + number; }

        private void RefreshRequestFulfillment()
        {
            if (State.mvp == null) return;
            foreach (GuestState g in State.guests)
                if (g.mvp != null && HotelHospitalityRules.Occupying(g)) g.luggageDelivered = HotelHospitalityRules.BagDelivered(State, g);
            foreach (MvpRequestState request in State.mvp.requests)
            {
                GuestState g = Guest(request.guestId);
                if (g == null || g.mvp == null || g.stage == "gone" || g.stage == "leaving")
                { if (request.status == "open") request.status = "cancelled"; continue; }
                if (request.status == "open" &&
                    ((request.kind == "luggage" && g.luggageDelivered) ||
                     (request.kind == "cleaning" && HotelHospitalityRules.CleanForRequest(Room(g.room))))) request.status = "fulfilled";
                // Towel/coffee fulfillment is explicitly marked by the successful delivery operation.
                if (request.status != "fulfilled" || request.rewarded) continue;
                request.rewarded = true;
                HospitalityMemory(g, "Выполнен запрос: " + HotelHospitalityRules.RequestName(request.kind), g.mvp.trait == "friendly" ? 7 : 5);
                if (request.kind == "towel") HotelOnboarding.Record(State, HotelTutorialSkill.ExtraTowel);
                if (request.kind == "luggage") HotelOnboarding.Record(State, HotelTutorialSkill.DeliverBag);
            }
            foreach (GuestState g in State.guests)
            {
                if (g.mvp == null) continue;
                g.towelRequested = State.mvp.requests.Exists(r => r.guestId == g.id && r.kind == "towel" && r.status == "open");
                if ((State.phase == "open" || State.phase == "closing") && !HotelDirector.ClockHeld(State)) ReconcileHospitalityComplaints(g, 0);
            }
        }

        private void ReconcileHospitalityComplaints(GuestState g, float dt)
        {
            if (g.mvp == null) return;
            List<MvpGuestIssue> issues = HotelHospitalityRules.Issues(State, g);
            foreach (MvpGuestIssue issue in issues)
            {
                MvpComplaintState complaint = State.mvp.complaints.Find(c => c.guestId == g.id && c.causeKey == issue.causeKey);
                if (complaint == null)
                {
                    if (State.mvp.complaints.Count >= 192)
                    {
                        int old = State.mvp.complaints.FindIndex(c => c.status == "resolved" && Guest(c.guestId) == null);
                        if (old < 0) continue;
                        State.mvp.complaints.RemoveAt(old);
                    }
                    complaint = new MvpComplaintState { id = MvpId("complaint"), guestId = g.id, category = issue.category, causeKey = issue.causeKey };
                    State.mvp.complaints.Add(complaint);
                    HospitalityMemory(g, issue.text, g.mvp.trait == "friendly" ? -4 : -6);
                }
                complaint.status = "active"; complaint.exposure += dt;
                if (complaint.escalation < 1 && complaint.exposure >= g.mvp.requestGrace * .5f)
                { complaint.escalation = 1; HospitalityMemory(g, "На жалобу долго не реагировали", -8); }
                if (complaint.escalation < 2 && complaint.exposure >= g.mvp.requestGrace * 1.3f)
                { complaint.escalation = 2; HospitalityMemory(g, "Проблему оставили без внимания", -12); }
            }
            foreach (MvpComplaintState c in State.mvp.complaints)
            {
                if (c.guestId != g.id || c.status != "active" || issues.Exists(x => x.causeKey == c.causeKey)) continue;
                // Passing through the corridor is not proof that the room was repaired.
                // A relocation is assessed on reaching the replacement room; checkout closes
                // unfinished room complaints at settlement without a recovery reward.
                if ((g.stage == "walking" || g.stage == "checkout") && c.category != "waiting" &&
                    c.category != "luggage" && !c.causeKey.StartsWith("request:", StringComparison.Ordinal)) continue;
                c.status = "resolved"; c.recoveredAt = State.mvp.elapsed;
                if (!c.recovered && !g.paid && g.stage != "gone" && g.stage != "leaving")
                { c.recovered = true; HospitalityMemory(g, "Персонал исправил проблему", g.mvp.trait == "friendly" ? 5 : 3); }
            }
        }

        private void HospitalitySettle(GuestState g)
        {
            if (g == null || g.mvp == null || g.paid || !HotelHospitalityRules.Occupying(g)) return;
            // Flush factual fulfillment before assessing departure; this never rerates the stay.
            RefreshRequestFulfillment();
            if (!g.luggageDelivered) HospitalityMemoryOnce(g, "Багаж так и не доставили", -12);
            int unfinished = State.mvp.requests.FindAll(r => r.guestId == g.id && r.status == "open" && r.kind != "luggage").Count;
            if (unfinished > 0) HospitalityMemoryOnce(g, "Остались невыполненные запросы", -8);
            int payment = checked(g.mvp.agreedTariff * g.mvp.billableNights);
            g.paid = true; State.cash += payment; State.earned += payment; State.served++;
            RoomState room = Room(g.room);
            if (room != null && room.guestId == g.id)
            {
                room.guestId = 0; room.bed = 1;
                if (room.towel) { room.towel = false; room.mvp.dirtyTowels = Math.Min(8, room.mvp.dirtyTowels + 1); }
            }
            MvpReservationState booking = ReservationFor(g);
            if (booking != null) booking.status = "completed";
            MvpGroupState group = GroupFor(g.mvp.groupId);
            if (group != null) group.completed++;
            RecordHospitalityReview(g);
            EndHospitalityService(g);
            g.stage = "leaving"; g.position = Reception;
            RetireBags(g.id);
            RefreshRequestFulfillment(); // A retired misdelivered bag/noise source also stops affecting neighbours.
            Log("Выезд #" + g.id + " · " + g.name + ": +" + payment + " (" + g.mvp.billableNights + " × " + g.mvp.agreedTariff + ")");
        }

        private void RefuseHospitalityGuest(GuestState g)
        {
            if (g == null || g.mvp == null || g.stage != "queue") return;
            MvpReservationState booking = ReservationFor(g);
            bool reserved = booking != null && booking.source != "walkin" && booking.source != "walkin_group";
            HospitalityMemory(g, reserved ? "Отель не выполнил подтверждённую бронь" : "Не удалось заселиться", reserved ? -65 : -20);
            if (booking != null) booking.status = "refused";
            MvpGroupState group = GroupFor(g.mvp.groupId);
            if (group != null) group.refused++;
            RecordHospitalityReview(g);
            EndHospitalityService(g);
            g.stage = "leaving"; RetireBags(g.id);
            RefreshRequestFulfillment();
            State.notice = g.name + " уходит без заселения.";
            UpdateHospitalityGroups();
        }

        private void CancelHospitalityBooking(MvpReservationState booking, string status)
        {
            if (!HotelHospitalityRules.Committed(booking)) return;
            booking.status = status;
            foreach (MvpScheduleState s in State.mvp.schedule)
                if (s.reservationId == booking.id && s.status == "planned") s.status = "cancelled";
            MvpGroupState group = GroupFor(booking.groupId);
            if (group != null) group.refused++;
            State.mvp.reputation = Mathf.Max(1, State.mvp.reputation - .2f);
            Log("Отменена подтверждённая бронь " + booking.id + ". Репутация снизилась.");
        }

        private void EndHospitalityService(GuestState g)
        {
            foreach (MvpRequestState r in State.mvp.requests) if (r.guestId == g.id && r.status == "open") r.status = "cancelled";
            foreach (MvpComplaintState c in State.mvp.complaints) if (c.guestId == g.id && c.status == "active")
            { c.status = "resolved"; c.recoveredAt = State.mvp.elapsed; }
            g.towelRequested = false;
        }

        private void FinishHospitality()
        {
            if (State.mvp == null) return;
            foreach (GuestState g in State.guests)
            {
                if (g.mvp == null) continue;
                if (g.stage == "queue") RefuseHospitalityGuest(g);
                else if (HotelHospitalityRules.Occupying(g) && g.mvp.departureDay <= State.day + 1) HospitalitySettle(g);
                // Continuing guests retain room, position, luggage, service clocks and contract.
                if (g.stage == "leaving") g.stage = "gone";
            }
            foreach (MvpScheduleState s in State.mvp.schedule)
            {
                if (s.day > State.day || s.status != "planned") continue;
                s.status = "cancelled";
                MvpReservationState r = State.mvp.reservations.Find(x => x.id == s.reservationId);
                if (r != null) CancelHospitalityBooking(r, "missed");
                else { MvpGroupState group = GroupFor(s.groupId); if (group != null) group.refused++; }
            }
            UpdateHospitalityGroups();
            PruneHospitality();
        }

        private void RecordHospitalityReview(GuestState g)
        {
            if (g.mvp.reviewRecorded) return;
            g.mvp.reviewRecorded = true;
            int stars = Mathf.Clamp(Mathf.RoundToInt(1 + g.satisfaction * .04f), 1, 5);
            // An unserved arrival cannot award a happy-stay review just because its initial
            // satisfaction was high. Breaking a confirmed promise is worse than refusing a walk-in.
            if (!g.paid && g.room == 0) stars = Math.Min(stars, string.IsNullOrEmpty(g.mvp.reservationId) ? 2 : 1);
            string text = "#" + g.id + " " + g.name + ": " + stars + "/5. ";
            var memories = g.memories.FindAll(m => m != "Попросил дополнительное полотенце");
            text += memories.Count == 0 ? "Спокойное проживание." : string.Join("; ", memories.GetRange(Math.Max(0, memories.Count - 5), Math.Min(5, memories.Count)).ToArray()) + ".";
            State.mvp.reviews.Add(new MvpReviewState { guestId = g.id, day = State.day, stars = stars, text = text });
            if (State.mvp.reviews.Count > 60) State.mvp.reviews.RemoveAt(0);
            State.reviews.Add(text); if (State.reviews.Count > 120) State.reviews.RemoveAt(0);
            float weighted = 0, weights = 0;
            for (int i = 0; i < State.mvp.reviews.Count; i++)
            { float w = 1f / (1 + (State.mvp.reviews.Count - i - 1) * .15f); weighted += State.mvp.reviews[i].stars * w; weights += w; }
            State.mvp.reputation = Mathf.Clamp(weighted / Math.Max(1, weights), 1, 5);
        }

        private void HospitalityMemory(GuestState g, string text, float change)
        {
            g.satisfaction = Mathf.Clamp(g.satisfaction + change, 0, 100);
            if (!g.memories.Contains(text)) { g.memories.Add(text); if (g.memories.Count > 64) g.memories.RemoveAt(0); }
        }
        private void HospitalityMemoryOnce(GuestState g, string text, float change)
        { if (!g.memories.Contains(text)) HospitalityMemory(g, text, change); }
        private MvpReservationState ReservationFor(GuestState g)
        { return g == null || g.mvp == null ? null : State.mvp.reservations.Find(r => r.id == g.mvp.reservationId); }
        private MvpGroupState GroupFor(string id)
        { return string.IsNullOrEmpty(id) ? null : State.mvp.groups.Find(g => g.id == id); }

        private void UpdateHospitalityGroups()
        {
            foreach (MvpGroupState group in State.mvp.groups)
            {
                bool future = State.mvp.schedule.Exists(s => s.groupId == group.id && s.status == "planned");
                bool waiting = State.guests.Exists(g => g.mvp != null && g.mvp.groupId == group.id && g.stage == "queue");
                bool staying = State.guests.Exists(g => g.mvp != null && g.mvp.groupId == group.id && HotelHospitalityRules.Occupying(g));
                if (staying) group.status = "staying";
                else if (waiting) group.status = "arriving";
                else if (future) group.status = "expected";
                else
                {
                    bool dirty = false;
                    foreach (string id in group.reservationIds)
                    {
                        MvpReservationState r = State.mvp.reservations.Find(x => x.id == id);
                        RoomState room = r == null ? null : Room(r.room);
                        if (room != null && room.guestId == 0 && (room.bed != 2 || !HotelHospitalityRules.CleanForRequest(room))) dirty = true;
                    }
                    group.status = dirty ? "cleanup" : "completed";
                }
            }
        }

        private void PruneHospitality()
        {
            if (State.mvp == null) return;
            // Terminal records are retained for the current/previous day where useful for Schedule.
            // A living guest's reward/complaint guards are never discarded.
            var retired = new HashSet<int>();
            foreach (GuestState g in State.guests)
                if (g.stage == "gone" && !State.rooms.Exists(r => r.guestId == g.id) &&
                    !State.items.Exists(i => i.ownerGuest == g.id)) retired.Add(g.id);
            foreach (MvpComplaintState c in State.mvp.complaints)
            {
                if (c.status != "active" || !c.causeKey.StartsWith("noise:", StringComparison.Ordinal)) continue;
                string[] parts = c.causeKey.Split(':');
                if (parts.Length == 3 && int.TryParse(parts[2], out int sourceGuest) && retired.Contains(sourceGuest))
                { c.status = "resolved"; c.recoveredAt = State.mvp.elapsed; }
            }
            State.mvp.requests.RemoveAll(r => retired.Contains(r.guestId) && r.status != "open");
            State.mvp.complaints.RemoveAll(c => retired.Contains(c.guestId) && c.status != "active");
            State.guests.RemoveAll(g => retired.Contains(g.id));
            foreach (MvpScheduleState s in State.mvp.schedule) if (retired.Contains(s.guestId)) s.guestId = 0;
            foreach (MvpReservationState r in State.mvp.reservations) if (retired.Contains(r.guestId)) r.guestId = 0;
            State.mvp.groups.RemoveAll(g => g.status == "completed" && g.arrivalDay < State.day - 1);
            State.mvp.schedule.RemoveAll(s => s.status != "planned" && s.day < State.day - 1 &&
                !State.guests.Exists(g => g.id == s.guestId) && !State.mvp.groups.Exists(g => g.id == s.groupId));
            State.mvp.reservations.RemoveAll(r => !HotelHospitalityRules.Committed(r) && r.departureDay < State.day &&
                !State.guests.Exists(g => g.mvp != null && g.mvp.reservationId == r.id) &&
                !State.mvp.groups.Exists(g => g.reservationIds.Contains(r.id)) && !State.mvp.schedule.Exists(s => s.reservationId == r.id));
            while (State.mvp.reviews.Count > 60) State.mvp.reviews.RemoveAt(0);
        }
    }
}
