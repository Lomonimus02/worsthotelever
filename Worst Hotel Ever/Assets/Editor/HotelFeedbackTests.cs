using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    public static class HotelFeedbackTests
    {
        public static List<string> RunAll()
        {
            var passed=new List<string>();
            var sim=new HotelSimulation();sim.Join(0);var s=sim.State;var p=s.players[0];
            var feedback=new HotelFeedback();
            s.nextGuest=4;p.held="existing";
            Require(feedback.Observe(s,0)==HotelCue.None,"Load replayed history");
            Require(feedback.Observe(s,0)==HotelCue.None,"Same host object duplicated events");
            s.nextGuest++;p.held="new";
            Require(feedback.Observe(s,0)==(HotelCue.Arrival|HotelCue.Item),"Host edges lost");
            Require(feedback.Observe(JsonUtility.FromJson<HotelState>(JsonUtility.ToJson(s)),0)==HotelCue.None,"Client replacement replayed events");
            passed.Add("Feedback observes scalar edges for in-place host and replaced client snapshots");

            s.day++;s.nextGuest++;p.held="other";
            Require(feedback.Observe(s,0)==HotelCue.None,"Day baseline replayed history");
            s.nextGuest++;Require(feedback.Observe(s,0)==HotelCue.Arrival,"First arrival of second day silent");
            s.worldId="different";s.nextGuest++;Require(feedback.Observe(s,0)==HotelCue.None,"New world replay");
            feedback.Reset();Require(feedback.Observe(s,0)==HotelCue.None,"Reconnect replay");
            Require(feedback.Observe(null,0)==HotelCue.None,"Null state");
            Require(feedback.Observe(s,99)==HotelCue.None,"Missing player");
            passed.Add("Load, reconnect, new world/day and missing players establish silent baselines");

            var room=s.rooms[1];feedback.Observe(s,0);room.bed=1;
            p.workTarget="bed_102";feedback.Observe(s,0);p.workTarget="";
            Require(feedback.Observe(s,0)==HotelCue.None,"Cancel reported success");
            p.workTarget="bed_102";feedback.Observe(s,0);p.workTarget="";room.bed=0;
            Require(feedback.Observe(s,0)==HotelCue.Complete,"Real bed completion silent");
            Require(feedback.Observe(s,0)==HotelCue.None,"Completion duplicated");
            p.workTarget="bed_102";feedback.Observe(s,0);feedback.CancelWork();
            feedback.Observe(s,0);feedback.Observe(JsonUtility.FromJson<HotelState>(JsonUtility.ToJson(s)),0);
            p.workTarget="";room.bed=2;
            Require(feedback.Observe(s,0)==HotelCue.None,"Local cancellation later claimed teammate completion");
            passed.Add("Only confirmed room results signal completion; cancellation and repeated snapshots do not");

            feedback.BeginWork();p.workTarget="bed_102";room.bed=1;feedback.Observe(s,0);
            p.workTarget="";room.bed=0;var completedBeforeInput=feedback.Observe(s,0);feedback.CancelWork();
            Require(completedBeforeInput==HotelCue.Complete,"Input cleanup erased an already received completion");
            passed.Add("Cancellation survives stale host/client snapshots; fresh intent rearms; result precedes input cleanup");

            foreach(string target in new[]{"sink_102","water_102","trash_102"})
            {
                feedback.BeginWork();room.leak=true;room.water=.3f;room.trash=true;p.workTarget=target;feedback.Observe(s,0);
                p.workTarget="";
                if(target.StartsWith("sink"))room.leak=false;
                if(target.StartsWith("water"))room.water=0;
                if(target.StartsWith("trash"))room.trash=false;
                Require(feedback.Observe(s,0)==HotelCue.Complete,"Missing "+target+" completion");
            }
            passed.Add("Repair, mop and trash completion derive from their own room result");

            feedback.BeginWork();p.workTarget="bed_102";room.bed=1;feedback.Observe(s,0);
            // A server-idle callback clears only local input intent, not the pending snapshot edge.
            feedback.Observe(s,0);p.workTarget="";room.bed=0;
            Require(feedback.Observe(s,0)==HotelCue.Complete,"Delayed world after server-idle feedback lost outcome");
            passed.Add("Server-idle before the next world snapshot preserves the pending outcome");

            var cart=new ItemState{id="cart_test",kind="cart",holder=0};s.items.Add(cart);p.held=cart.id;
            var bag=new ItemState{id="cargo_b",kind="bag",placedRoom=-1};s.items.Add(bag);feedback.Reset();
            Require(feedback.Observe(s,0)==HotelCue.None,"Loaded cart replayed cargo");
            bag.placedRoom=101;Require(feedback.Observe(s,0)==HotelCue.Item,"Cart delivery silent");
            bag.placedRoom=-1;Require(feedback.Observe(s,0)==HotelCue.Item,"Cart loading silent");
            var bag2=new ItemState{id="cargo_a",kind="bag",placedRoom=-1};s.items.Add(bag2);
            Require(feedback.Observe(s,0)==HotelCue.Item,"Second cargo silent");
            s.items.Reverse();Require(feedback.Observe(s,0)==HotelCue.None,"Item order emitted false cargo cue");
            p.held="";cart.holder=1;feedback.Observe(s,0);bag.placedRoom=102;
            Require(feedback.Observe(s,0)==HotelCue.None,"Remote cart emitted local item cue");
            passed.Add("Held cart cargo loading/delivery emits one cue; baseline, reordering and remote cart are silent");

            string before=JsonUtility.ToJson(s);feedback.Observe(s,0);
            Require(before==JsonUtility.ToJson(s),"Feedback mutated simulation");
            var poses=new HashSet<Vector3>();
            foreach(string target in new[]{"bed_102","sink_102","water_102","trash_102"})
            {
                HotelFeedback.HandPose(target,.27f,true,out var offset,out var rotation);poses.Add(offset);
                Require(offset.magnitude<.1f&&Mathf.Abs(offset.z)<.001f&&Quaternion.Angle(rotation,Quaternion.identity)<30,"Hand motion excessive");
                HotelFeedback.HandPose(target,.27f,false,out offset,out rotation);
                Require(offset==Vector3.zero&&rotation==Quaternion.identity,"Motion-off not neutral");
            }
            Require(poses.Count==4,"Work gestures indistinguishable");
            HotelFeedback.HandPose("",1,true,out var idle,out var neutral);Require(idle==Vector3.zero&&neutral==Quaternion.identity,"Idle not neutral");
            passed.Add("Read-only hand poses are distinct, bounded and neutral when disabled/idle");

            foreach(string kind in new[]{"step","arrival","item","complete","cloth","repair","mop","trash","leak"})
            {
                var samples=HotelAudio.Samples(kind,.8f);float energy=0;
                foreach(float sample in samples){Require(!float.IsNaN(sample)&&!float.IsInfinity(sample)&&Mathf.Abs(sample)<=.8f,"Invalid audio sample");energy+=sample*sample;}
                Require(energy>.01f&&Mathf.Abs(samples[0])<.0001f&&Mathf.Abs(samples[samples.Length-1])<.0001f,"Silent or unbounded clip edges");
            }
            passed.Add("All nine original audio assets have finite non-silent bounded samples and quiet loop edges");
            return passed;
        }
        static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    }
}
