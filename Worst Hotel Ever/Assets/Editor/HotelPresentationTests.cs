using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WorstHotel
{
    public static class HotelPresentationTests
    {
        public static List<string> RunAll()
        {
            var passed = new List<string>();
            Require(HotelSteam.Protocol==HotelSession.Protocol,"Steam/IP protocol versions diverged");
            var sim = new HotelSimulation(); sim.Join(0); sim.Join(1);
            string before = JsonUtility.ToJson(sim.State);
            Require(HotelPresentation.FocusText(sim.State,0,"bed_102","").Contains("снять"),"Dirty bed did not explain hold");
            Require(HotelPresentation.FocusText(sim.State,0,"bed_104","").Contains("чистое бельё"),"Bare bed did not explain supply");
            Require(HotelPresentation.FocusText(sim.State,0,"sink_101","").Contains("исправна"),"Healthy sink asks for repair");
            Require(HotelPresentation.RoomProblems(sim.State.rooms[1]).Contains("Грязное"),"Dirty status missing");
            Require(!HotelPresentation.TryTarget(sim.State,"unknown_101",out _),"Unknown target points at spawn");
            Require(!HotelPresentation.TryTarget(sim.State,"bed_999",out _),"Nonexistent room target");
            Require(HotelPresentation.TryTarget(sim.State,"bed_102",out Vector3 target)&&target==HotelLayout.Target("bed_102"),"Wrong target");
            Require(before==JsonUtility.ToJson(sim.State),"Presentation mutated world");
            passed.Add("Context prompts and target lookup are read-only and world-derived");

            var room=sim.State.rooms[1];room.guestId=1;
            var guest=new GuestState{id=1,room=102,name="Тестер",trait="Терпеливый",stage="staying",towelRequested=true};
            sim.State.guests.Add(guest);
            var bag=new ItemState{id="luggage_1",kind="bag",ownerGuest=1};sim.State.items.Add(bag);
            Require(HotelPresentation.HeldLabel(sim.State,bag).Contains("Тестер → № 102"),"Bag owner missing");
            Require(HotelPresentation.FocusText(sim.State,0,"bed_102","").Contains("дождитесь выезда"),"Occupied bed suggests unavailable work");
            Require(HotelPresentation.GuestIssues(sim.State,guest).Contains("Дополнительное полотенце"),"Request missing from inspector");
            Require(HotelPresentation.RoomProblems(room).Contains("после выезда"),"Used occupied bed called ready");
            passed.Add("Guest inspector, occupied beds and bag labels explain real ownership");

            string directory=Path.Combine(Path.GetTempPath(),"WorstHotelReadLock-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path=Path.Combine(directory,"hotel.json");
                var state=new HotelSimulation().State;
                HotelSaveStore.Save(state,path);state.cash=350;HotelSaveStore.Save(state,path);
                string primary=File.ReadAllText(path),backup=File.ReadAllText(path+".bak");
                using(var locked=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None))
                {
                    bool rejected=false;
                    try{HotelSaveStore.Load(path);}catch(IOException){rejected=true;}
                    Require(rejected,"Locked healthy primary silently loaded stale backup");
                }
                Require(File.ReadAllText(path)==primary&&File.ReadAllText(path+".bak")==backup,"Read lock altered save files");
                Require(HotelSaveStore.Load(path).cash==350,"Healthy latest save lost");
                passed.Add("Transient read lock does not roll back a healthy save to its backup");
            }
            finally{Directory.Delete(directory,true);}
            return passed;
        }
        static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    }
}
