using System.Linq;
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class HotelUI
    {
        static readonly Rect AlertBounds=new Rect(403,25,634,92);
        void HudCard(Rect r,Color? line=null)
        {
            Box(new Rect(r.x+2,r.y+3,r.width,r.height),new Color(0,0,0,.18f));
            Box(r,new Color(.085f,.12f,.115f,.88f));
            if(line.HasValue)Box(new Rect(r.x,r.y,3,r.height),line.Value);
        }
        void HUD()
        {
            if(Game.Panel!="")return;
            var alert=HotelHudModel.Alert(S,Game.Session.LocalId,false);
            var local=Danger?HotelDangerRules.Crew(S,Game.Session.LocalId):null;
            // No permanent top strip, empty-hands panel, FPS counter or field manual.
            if(local==null||local.life=="healthy") {
                Box(new Rect(718,448,4,4),paper);
                HudCard(new Rect(1118,28,290,57));
                Text(new Rect(1133,35,260,23),"ДЕНЬ "+S.day,small,gold);
                Text(new Rect(1133,57,260,23),HotelHudModel.Clock(S),small);
            }
            if(local!=null) {
                var colleague=S.danger.crew.Find(c=>c.joined&&c.slot!=local.slot);
                float top=colleague==null?784:756;
                HudCard(new Rect(32,top,222,867-top),local.life=="healthy"?brass:red);
                Text(new Rect(47,top+10,190,25),local.life=="healthy"?"ЗДОРОВЬЕ  "+Mathf.CeilToInt(local.health):local.life=="downed"?"ВЫ РАНЕНЫ":"ВЫ ПОГИБЛИ",bold);
                Box(new Rect(48,top+45,188,5),new Color(.3f,.32f,.27f));
                Box(new Rect(48,top+45,188*Mathf.Clamp01(local.health/100f),5),local.health<35?gold:green);
                Text(new Rect(47,top+57,193,23),"Аптечки команды: "+S.danger.medkits,small,muted);
                if(colleague!=null) {
                    string status=colleague.life=="dead"?"погиб":colleague.life=="downed"?"нужна помощь":Mathf.CeilToInt(colleague.health)+" / 100";
                    bool online=S.players.Any(p=>HotelDangerRules.Slot(p.id)==colleague.slot);
                    Text(new Rect(47,top+82,194,23),"Коллега: "+(online?status:"вне сети"),small,colleague.life=="healthy"?muted:gold);
                }
            }
            string goal=HotelHudModel.Objective(S,Game.Session.LocalId);
            if(goal!=""&&alert.kind=="") {
                float h=normal.CalcHeight(new GUIContent(goal),305)+46;
                HudCard(new Rect(32,28,339,h),brass);
                Text(new Rect(47,37,305,23),"СЛЕДУЮЩИЙ ШАГ",small,gold);
                Text(new Rect(47,64,305,h-32),goal,normal);
                var hint=HotelOnboarding.GetHint(S,Game.Session.LocalId);
                DrawTarget(hint?.targetId,goal,false);
            }
            else if(alert.target!="")DrawTarget(alert.target,"Сюда",true);
            if(Game.Held!=null) {
                string held=HotelPresentation.HeldLabel(S,Game.Held);
                float h=normal.CalcHeight(new GUIContent(held),318)+40;
                HudCard(new Rect(1058,819-h,350,h));
                Text(new Rect(1073,829-h,318,h-25),held,normal);
                Text(new Rect(1073,797,318,23),"Q  ·  положить",small,gold);
            }
            HudCard(new Rect(1222,839,186,32));
            Text(new Rect(1236,844,165,24),"TAB  ·  журнал",small,gold);
            if(!string.IsNullOrEmpty(Game.FocusLabel)&&local?.life!="dead") {
                string label=HotelHudModel.Focus(Game.FocusLabel);
                string key=HotelHudModel.FocusKey(Game.FocusLabel,Game.FocusId);
                float keyWidth=Mathf.Max(68,bold.CalcSize(new GUIContent(key)).x+6);
                float textWidth=590-keyWidth;
                float h=Mathf.Max(47,normal.CalcHeight(new GUIContent(label),textWidth)+23);
                float top=784-h;
                HudCard(new Rect(403,top,634,h));
                Text(new Rect(419,top+11,keyWidth,h-14),key,bold,gold);
                Text(new Rect(431+keyWidth,top+11,textWidth,h-14),label,normal);
            }
            if(Game.LocalPlayer!=null&&Game.LocalPlayer.workTarget!="") {
                Box(new Rect(543,794,354,7),new Color(.16f,.19f,.16f));
                Box(new Rect(543,794,354*Mathf.Clamp01(Game.LocalPlayer.workProgress),7),gold);
            }
            var ping=S.mvp?.pings.LastOrDefault();
            if(ping!=null)DrawTarget(ping.target,ping.playerId==Game.Session.LocalId?"Ваша отметка":"Коллега отмечает",false,true);
        }
        void DrawTarget(string id,string caption,bool urgent,bool pingMarker=false)
        {
            if(string.IsNullOrEmpty(id)||!HotelPresentation.TryTarget(S,id,out Vector3 target))return;
            // A ping at the current objective owns its marker, avoiding two overprinted captions.
            if(!pingMarker&&S.mvp?.pings.LastOrDefault()?.target==id)return;
            Vector3 pixel=Game.View.WorldToScreenPoint(target+Vector3.up*.3f);
            float scale=Mathf.Min(Screen.width/1440f,Screen.height/900f);
            float x=(pixel.x-(Screen.width-1440*scale)*.5f)/scale;
            float y=(Screen.height-pixel.y-(Screen.height-900*scale)*.5f)/scale;
            float distance=Vector3.Distance(Game.View.transform.position,target);
            // Markers outside the useful view become a compact direction note, not a side panel.
            bool outside=pixel.z<=0||x<390||x>1040||y<190||y>610;
            if(outside) {
                Vector3 delta=target-Game.View.transform.position;delta.y=0;
                Vector3 forward=Game.View.transform.forward;forward.y=0;
                float angle=Vector3.SignedAngle(forward,delta,Vector3.up);
                string direction=Mathf.Abs(angle)>135?"позади":angle>30?"справа":angle< -30?"слева":"впереди";
                if(urgent)TextShadow(new Rect(585,124,315,27),direction+" · "+Mathf.CeilToInt(distance)+" м",small,gold);
                else if(pingMarker) {
                    string label=caption+" · "+MvpTargetName(id);
                    float height=small.CalcHeight(new GUIContent(label),310);
                    HudCard(new Rect(32,697-height,339,height+43),brass);
                    Text(new Rect(47,704-height,310,height),label,small,gold);
                    Text(new Rect(47,710,310,24),direction+" · "+Mathf.CeilToInt(distance)+" м",small,paper);
                } else {
                    string goal=HotelHudModel.Objective(S,Game.Session.LocalId);
                    float top=28+normal.CalcHeight(new GUIContent(goal),305)+52;
                    TextShadow(new Rect(46,top,310,24),direction+" · "+Mathf.CeilToInt(distance)+" м",small,gold);
                }
                return;
            }
            Box(new Rect(x-5,y-5,10,10),gold);Box(new Rect(x-2,y-2,4,4),ink);
            TextShadow(new Rect(x-74,y+12,160,26),Mathf.CeilToInt(distance)+" м",small,paper);
            if(pingMarker)
                TextShadow(new Rect(x-96,y+35,260,24),caption,small,gold);
        }
        void TextShadow(Rect r,string value,GUIStyle style,Color color)
        {
            Text(new Rect(r.x+1,r.y+1,r.width,r.height),value,style,new Color(0,0,0,.95f));
            Text(r,value,style,color);
        }
        void DrawAlert()
        {
            var alert=HotelHudModel.Alert(S,Game.Session.LocalId,Game.Panel!="");
            if(alert.kind=="")return;
            float titleHeight=Mathf.Max(25,bold.CalcHeight(new GUIContent(alert.title),600));
            float detailHeight=Mathf.Max(23,small.CalcHeight(new GUIContent(alert.detail),600));
            var r=new Rect(AlertBounds.x,AlertBounds.y,AlertBounds.width,titleHeight+detailHeight+23);
            Box(r,alert.urgent?new Color(.35f,.085f,.075f,.96f):new Color(.24f,.18f,.10f,.96f));
            Box(new Rect(r.x,r.y,4,r.height),gold);
            Text(new Rect(r.x+16,r.y+9,600,titleHeight),alert.title,bold,paper);
            Text(new Rect(r.x+16,r.y+12+titleHeight,600,detailHeight),alert.detail,small,paper);
        }
        void DrawToast()
        {
            if(Game.Toast==""||(!Game.Playing&&Game.Panel!="steam"))return;
            // Day briefings belong in the journal; they need not interrupt the first look at the hotel.
            if(!HotelHudModel.ShowToast(Game.Playing?S:null,Game.Toast))return;
            float height=Mathf.Max(38,small.CalcHeight(new GUIContent(Game.Toast),584)+18);
            float y=Game.Panel==""?818:796;
            HudCard(new Rect(412,y,616,height),brass);
            Text(new Rect(428,y+8,584,height-10),Game.Toast,small);
        }
    }
}
