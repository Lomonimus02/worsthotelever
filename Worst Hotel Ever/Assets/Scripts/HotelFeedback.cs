using System;
using UnityEngine;

namespace WorstHotel
{
    [Flags] public enum HotelCue { None=0, Arrival=1, Item=2, Complete=4 }

    // Snapshot edges, not button presses: a rejected command must never sound successful.
    // Store scalars, not references: host state is mutated in place while client state is replaced.
    public sealed class HotelFeedback
    {
        string world, held, work;
        readonly string[] cargo = new string[4], previousCargo = new string[4];
        int day, nextGuest, bed, dirtyTowels;
        bool initialized, leak, trash, suppressWork, equipmentFault, utilityFault;
        float water, dirt;
        ulong playerId;
        public void Reset(){initialized=false;suppressWork=false;world=null;held=work="";}
        public void BeginWork(){suppressWork=false;}
        public void CancelWork(){work="";suppressWork=true;}
        public HotelCue Observe(HotelState state, ulong localId)
        {
            var player=state?.players.Find(p=>p.id==localId);
            if(player==null){Reset();return HotelCue.None;}
            bool baseline=!initialized||world!=state.worldId||day!=state.day||playerId!=localId;
            bool cart=state.items.Exists(i=>i.id==player.held&&i.kind=="cart"&&!i.consumed&&i.holder==(long)localId);
            Array.Clear(cargo,0,cargo.Length);
            int cargoCount=0;
            if(cart)foreach(var item in state.items)
                if(item.kind=="bag"&&!item.consumed&&item.placedRoom==-1)
                {
                    if(cargoCount<cargo.Length)cargo[cargoCount++]=item.id;
                }
            Array.Sort(cargo,StringComparer.Ordinal);
            bool cargoChanged=false;
            for(int i=0;i<cargo.Length;i++)if(cargo[i]!=previousCargo[i])cargoChanged=true;
            HotelCue cue=HotelCue.None;
            if(!baseline)
            {
                if(state.nextGuest>nextGuest)cue|=HotelCue.Arrival;
                if((player.held??"")!=held)cue|=HotelCue.Item;
                else if(cart&&cargoChanged)cue|=HotelCue.Item;
                if(!string.IsNullOrEmpty(work)&&work!=player.workTarget)
                {
                    var room=Room(state,work);
                    bool changed=room!=null&&
                        ((work.StartsWith("bed_")&&((bed==1&&room.bed==0)||(bed==0&&room.bed==2)))||
                        (work.StartsWith("sink_")&&leak&&!room.leak)||
                        (work.StartsWith("trash_")&&trash&&!room.trash)||
                        (work.StartsWith("water_")&&water>0&&room.water<=.001f));
                    if(state.mvp!=null)
                    {
                        if(work=="utility_water")changed=utilityFault&&!state.mvp.utilities.waterFault;
                        else if(work=="utility_power")changed=utilityFault&&!state.mvp.utilities.powerFault;
                        else if(work=="coffee")changed=player.held!=held&&state.items.Exists(i=>i.id==player.held&&i.kind=="coffee"&&!i.consumed);
                        else if(room?.mvp!=null)
                        {
                            string kind=work.Substring(0,work.LastIndexOf('_'));
                            var equipment=room.mvp.equipment.Find(e=>e.kind==kind);
                            if(equipment!=null)changed=equipmentFault&&!equipment.localFault;
                            else if(kind=="clean")changed=dirt>.05f&&room.mvp.dirt<=.05f;
                            else if(kind=="dirtytowel")changed=dirtyTowels>room.mvp.dirtyTowels;
                        }
                    }
                    if(changed)cue|=HotelCue.Complete;
                }
            }
            initialized=true;world=state.worldId;day=state.day;playerId=localId;
            held=player.held??"";work=suppressWork?"":player.workTarget??"";nextGuest=state.nextGuest;
            Array.Copy(cargo,previousCargo,cargo.Length);
            var current=Room(state,work);
            if(current!=null){bed=current.bed;leak=current.leak;trash=current.trash;water=current.water;}
            if(current?.mvp!=null)
            {
                dirt=current.mvp.dirt;dirtyTowels=current.mvp.dirtyTowels;
                string kind=work.Substring(0,work.LastIndexOf('_'));
                equipmentFault=current.mvp.equipment.Find(e=>e.kind==kind)?.localFault??false;
            }
            if(state.mvp!=null)utilityFault=work=="utility_water"?state.mvp.utilities.waterFault:work=="utility_power"&&state.mvp.utilities.powerFault;
            return cue;
        }
        static RoomState Room(HotelState state,string target)
        {
            if(string.IsNullOrEmpty(target))return null;
            int split=target.LastIndexOf('_');
            return split>=0&&int.TryParse(target.Substring(split+1),out int n)?state.rooms.Find(r=>r.number==n):null;
        }
        public static string WorkKind(string target)
        {
            if(string.IsNullOrEmpty(target))return "";
            if(target.StartsWith("bed_"))return "cloth";
            if(target.StartsWith("sink_"))return "repair";
            if(target.StartsWith("water_"))return "mop";
            if(target.StartsWith("trash_"))return "trash";
            if(target.StartsWith("tv_")||target.StartsWith("lamp_")||target.StartsWith("utility_"))return "repair";
            if(target.StartsWith("toilet_")||target.StartsWith("clean_"))return "mop";
            if(target.StartsWith("dirtytowel_")||target=="coffee")return "cloth";
            return "";
        }
        public static void HandPose(string target,float time,bool enabled,out Vector3 offset,out Quaternion rotation)
        {
            offset=Vector3.zero;rotation=Quaternion.identity;
            if(!enabled)return;
            float wave=Mathf.Sin(time*9),slow=Mathf.Sin(time*5);
            switch(WorkKind(target))
            {
                case "cloth":offset=new Vector3(slow*.055f,Mathf.Abs(wave)*.035f,0);rotation=Quaternion.Euler(wave*5,slow*7,slow*6);break;
                case "repair":offset=new Vector3(wave*.015f,.025f+slow*.015f,0);rotation=Quaternion.Euler(wave*7,0,wave*13);break;
                case "mop":offset=new Vector3(slow*.07f,-.025f,0);rotation=Quaternion.Euler(slow*9,slow*12,0);break;
                case "trash":offset=new Vector3(slow*.02f,Mathf.Abs(wave)*.055f,0);rotation=Quaternion.Euler(wave*8,0,slow*5);break;
            }
        }
    }
}
