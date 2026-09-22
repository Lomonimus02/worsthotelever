using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    // Original synthesized mono assets; all sources/clips allocated once per game, never per event.
    public sealed class HotelAudio : MonoBehaviour
    {
        readonly Dictionary<string,AudioClip> clips=new Dictionary<string,AudioClip>();
        readonly AudioSource[] leaks=new AudioSource[6];
        AudioSource effects, workSource;
        string workKind="";
        public int CueCount {get;private set;}
        public int CompletionCount {get;private set;}
        public int ArrivalCount {get;private set;}
        public string ActiveWork=>workKind;
        public int ActiveLeaks {get {int n=0;foreach(var source in leaks)if(source!=null&&source.isPlaying)n++;return n;}}
        public int ClipCount=>clips.Count;
        void Awake()
        {
            foreach(string kind in new[]{"step","arrival","item","complete","cloth","repair","mop","trash","leak"})
            {
                float seconds=kind=="arrival"?.48f:kind=="complete"?.3f:kind=="step"?.12f:kind=="item"?.16f:.8f;
                float[] data=Samples(kind,seconds);
                var clip=AudioClip.Create("WHE original "+kind,data.Length,1,22050,false);clip.SetData(data,0);clips.Add(kind,clip);
            }
            effects=Source("Local effects",false);workSource=Source("Confirmed work",false);workSource.loop=true;
            for(int i=0;i<leaks.Length;i++)
            {
                var source=Source("Leak "+(101+i),true);source.loop=true;source.clip=clips["leak"];
                source.transform.position=HotelLayout.RoomTarget("sink",101+i);leaks[i]=source;
            }
        }
        AudioSource Source(string label,bool spatial)
        {
            var node=new GameObject(label);node.transform.SetParent(transform,false);
            var source=node.AddComponent<AudioSource>();source.playOnAwake=false;source.volume=spatial?.38f:.65f;
            source.spatialBlend=spatial?1:0;source.dopplerLevel=0;
            source.rolloffMode=AudioRolloffMode.Linear;source.minDistance=1;source.maxDistance=9;
            return source;
        }
        public void Apply(HotelState state,HotelCue cues,string confirmedWork,bool ambient)
        {
            if(state==null){Silence();return;}
            if((cues&HotelCue.Arrival)!=0){Play("arrival");ArrivalCount++;}
            if((cues&HotelCue.Complete)!=0){Play("complete");CompletionCount++;}
            else if((cues&HotelCue.Item)!=0)Play("item");
            string kind=HotelFeedback.WorkKind(confirmedWork);
            if(kind!=workKind)
            {
                workSource.Stop();workKind=kind;
                workSource.clip=kind==""?null:clips[kind];
                if(kind!="")workSource.Play();
            }
            for(int i=0;i<leaks.Length;i++)
            {
                var room=state.rooms.Find(r=>r.number==101+i);
                bool active=ambient&&room!=null&&(room.mvp==null||room.mvp.owned)&&room.leak&&(state.mvp==null||!state.mvp.utilities.waterFault);
                if(active&&!leaks[i].isPlaying)leaks[i].Play();
                else if(!active&&leaks[i].isPlaying)leaks[i].Stop();
            }
        }
        public void Play(string kind){if(effects!=null&&clips.TryGetValue(kind,out var clip)){effects.PlayOneShot(clip);CueCount++;}}
        public void Silence()
        {
            if(effects!=null)effects.Stop();if(workSource!=null)workSource.Stop();workKind="";
            foreach(var source in leaks)if(source!=null)source.Stop();
        }
        public static float[] Samples(string kind,float seconds)
        {
            int count=Math.Max(2,(int)(22050*seconds));var data=new float[count];uint random=8171;
            float smooth=0;
            for(int i=0;i<count;i++)
            {
                float t=i/22050f,phase=i/(float)(count-1);
                random=unchecked(random*1664525+1013904223);float noise=((random>>8)/16777215f)*2-1;
                smooth=Mathf.Lerp(smooth,noise,.12f);
                float envelope=Mathf.Sin(Mathf.PI*phase);envelope*=envelope;
                float value;
                switch(kind)
                {
                    case "arrival":value=(Mathf.Sin(t*660*Mathf.PI*2)+Mathf.Sin(t*880*Mathf.PI*2)*.45f)*.18f;break;
                    case "complete":value=(Mathf.Sin(t*(phase<.45f?600:800)*Mathf.PI*2))*.16f;break;
                    case "item":value=smooth*.6f+Mathf.Sin(t*180*Mathf.PI*2)*.09f;break;
                    case "step":value=smooth*.65f+Mathf.Sin(t*95*Mathf.PI*2)*.08f;break;
                    case "repair":value=Mathf.Sin(t*1240*Mathf.PI*2)*Mathf.Pow(Mathf.Max(0,Mathf.Cos(t*Mathf.PI*10)),12)*.13f+smooth*.12f;break;
                    case "mop":value=smooth*.40f*(.4f+.6f*Mathf.Abs(Mathf.Sin(t*9)));break;
                    case "trash":value=noise*.09f+smooth*.22f;break;
                    case "leak":value=smooth*.3f+Mathf.Sin(t*(740+100*Mathf.Sin(t*18))*Mathf.PI*2)*Mathf.Pow(Mathf.Max(0,Mathf.Cos(t*24)),18)*.09f;break;
                    default:value=smooth*.25f;break;
                }
                data[i]=Mathf.Clamp(value*envelope,-.8f,.8f);
            }
            return data;
        }
        void OnDisable(){Silence();}
        void OnDestroy()
        {
            Silence();
            if(effects!=null)Destroy(effects.gameObject);if(workSource!=null)Destroy(workSource.gameObject);
            foreach(var source in leaks)if(source!=null)Destroy(source.gameObject);
            foreach(var clip in clips.Values)if(clip!=null)Destroy(clip);clips.Clear();
        }
    }
}
