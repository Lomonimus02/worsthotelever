using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WorstHotel
{
    // Opt-in standalone test driver; no debug shortcuts are exposed to normal players.
    public sealed class HotelSmokeTest : MonoBehaviour
    {
        readonly List<string> checks=new List<string>();
        readonly List<string> errors=new List<string>();
        string dir, role;
        HotelGame game;
        void Awake(){Application.logMessageReceived+=Log;}
        void Log(string message,string stack,LogType type){if(type==LogType.Exception||type==LogType.Error||type==LogType.Assert)errors.Add(message+"\n"+stack);}
        IEnumerator Start()
        {
            game=HotelGame.Instance;dir=Path.GetFullPath(Path.Combine(Application.dataPath,"..","..","..","TestResults"));Directory.CreateDirectory(dir);
            float deadline=Time.realtimeSinceStartup+20;
            while(!game.Session.Connected && Time.realtimeSinceStartup<deadline)yield return null;
            if(!game.Session.Connected){Finish(false,"No network session after 20 seconds");yield break;}
            role=game.Session.IsHost?"host":"client";
            checks.Add("CONNECTED "+role+" id="+game.Session.LocalId);
            yield return new WaitForSecondsRealtime(3);
            if(game.Session.State.rooms.Count!=4){Finish(false,"Expected four rooms");yield break;}
            checks.Add("FOUR_ROOMS");
            if(game.Controller==null||game.View==null){Finish(false,"First person controller or camera missing");yield break;}
            checks.Add("FIRST_PERSON_CREATED");
            int renderers=FindObjectsByType<Renderer>(FindObjectsSortMode.None).Length;
            if(renderers<50){Finish(false,"Hotel geometry absent");yield break;}
            checks.Add("WORLD_RENDERERS "+renderers);
            foreach(var renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))foreach(var material in renderer.sharedMaterials)
                if(material!=null&&(material.shader==null||material.shader.name=="Hidden/InternalErrorShader"))errors.Add("Missing shader: "+renderer.name);
            checks.Add("SHADER_INSPECTION");
            // The planned static navigation axes must also be open in the actual built geometry.
            foreach(int room in new[]{101,102,103,104}) {
                Vector3 a=new Vector3(0,1,HotelLayout.RoomCenter(room).z);
                Vector3 b=HotelLayout.RoomCenter(room)+Vector3.up;
                if(Physics.Linecast(a,b,out var blocked) && blocked.collider.GetComponentInParent<HotelTarget>()==null)
                    errors.Add("Room route blocked "+room+": "+blocked.collider.name);
            }
            checks.Add("ROOM_DOOR_ROUTES_INSPECTED");
            game.OpenPanel("");
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(dir,role+"-hotel.png"));
            deadline=Time.realtimeSinceStartup+25;
            while(game.Session.State.players.Count<2&&Time.realtimeSinceStartup<deadline)yield return null;
            if(game.Session.State.players.Count<2){Finish(false,"Second player did not join");yield break;}
            checks.Add("TWO_PLAYERS_REPLICATED");
            if(game.Session.IsHost) {
                game.Session.Save();
                var loaded=HotelSaveStore.Load(game.Session.SavePath);
                if(loaded.cash!=game.Session.State.cash||loaded.day!=game.Session.State.day){Finish(false,"Save roundtrip mismatch");yield break;}
                checks.Add("SAVE_LOAD_ROUNDTRIP");
                // Change a real replicated field and verify it on the other process.
                game.Session.State.notice="WHE_SMOKE_SNAPSHOT";
                yield return new WaitForSecondsRealtime(15);
                checks.Add("HOST_WORLD_STABLE");
            } else {
                var original=game.Session.State.cash;
                game.Session.Send(new HotelCommand("upgrade","beds"));
                yield return new WaitForSecondsRealtime(1);
                if(game.Session.State.cash!=original){Finish(false,"Client could buy a host-only upgrade");yield break;}
                checks.Add("CLIENT_ADMIN_REJECTED");
                float t=Time.realtimeSinceStartup;Vector3 start=game.Controller.transform.position;
                while(Time.realtimeSinceStartup-t<.45f){game.Controller.Move(Vector3.right*Time.unscaledDeltaTime);yield return null;}
                yield return new WaitForSecondsRealtime(.5f);
                var authoritative=game.Session.State.players.Find(p=>p.id==game.Session.LocalId);
                if(authoritative==null||Vector3.Distance(authoritative.position,game.Controller.transform.position)>.8f){Finish(false,"Movement replication mismatch");yield break;}
                checks.Add("CLIENT_MOVEMENT_REPLICATED");
                deadline=Time.realtimeSinceStartup+12;
                while(game.Session.State.notice!="WHE_SMOKE_SNAPSHOT"&&Time.realtimeSinceStartup<deadline)yield return null;
                if(game.Session.State.notice!="WHE_SMOKE_SNAPSHOT"){Finish(false,"Host snapshot was not received");yield break;}
                checks.Add("HOST_SNAPSHOT_RECEIVED");
                yield return new WaitForSecondsRealtime(5);
            }
            Finish(errors.Count==0,errors.Count==0?"All smoke checks passed":"Runtime errors");
        }
        void Finish(bool success,string reason)
        {
            checks.Insert(0,success?"PASS":"FAIL");checks.Add(reason);checks.AddRange(errors);
            if(dir==null)dir=Application.persistentDataPath;
            File.WriteAllLines(Path.Combine(dir,(role??"unknown")+"-smoke.txt"),checks);
            Debug.Log("WHE_SMOKE "+(success?"PASS":"FAIL")+" "+reason);
            game?.Session.Disconnect();Application.Quit(success?0:1);
        }
        void OnDestroy(){Application.logMessageReceived-=Log;}
    }
}
