using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WorstHotel
{
    public sealed partial class HotelUI
    {
        public readonly HotelTeachingHints TeachingHints=new HotelTeachingHints();
        public int HudElementCountForTest { get; private set; }
        public bool TeachingHintDrawnForTest { get; private set; }
        public bool TabletDrawnForTest { get; private set; }
        public string AlertInTabletForTest { get; private set; }="";
        public string SaveErrorInTabletForTest { get; private set; }="";
        readonly List<string> tabletNotices=new List<string>();
        string tipWorld="",lastTabletNotice="",lastFocus="";
        bool teachingLoaded,welcomeShown;

        void LateUpdate()
        {
            if(Game?.Session==null)return;
            if(!teachingLoaded) {
                TeachingHints.Enabled=Game.Automated||PlayerPrefs.GetInt("localTeachingHints",1)==1;
                teachingLoaded=true;
            }
            float now=Time.unscaledTime;
            if(!Game.Playing) { TeachingHints.Tick(now,"","","",false);tipWorld="";return; }
            if(tipWorld!=S.worldId) {
                tipWorld=S.worldId;TeachingHints.Reset(now);welcomeShown=false;
                tabletNotices.Clear();lastTabletNotice="";lastFocus="";
            }
            if(Game.InputActive&&!string.IsNullOrEmpty(Game.FocusLabel))lastFocus=Game.FocusLabel;
            if(Game.Toast!=""&&Game.Toast!=lastTabletNotice) {
                lastTabletNotice=Game.Toast;tabletNotices.Insert(0,Game.Toast);
                if(tabletNotices.Count>12)tabletNotices.RemoveAt(12);
            }
            bool allowed=Game.InputActive&&HotelHudModel.ShowLesson(S,Game.Session.LocalId);
            var hint=HotelOnboarding.GetHint(S,Game.Session.LocalId,true);
            string key=!welcomeShown?"welcome":hint?.title??"";
            string heading=!welcomeShown?"НА СВЯЗИ · СЛУЖБА ПЕРСОНАЛА":hint?.title??"";
            string body=!welcomeShown?"Tab — ваш рабочий планшет. Все дела и управление — там. E — взять или работать, Shift — бежать.":hint?.body??"";
            TeachingHints.Tick(now,key,heading,body,allowed);
            if(TeachingHints.CurrentKey=="welcome")welcomeShown=true;
            if(Game.InputActive&&Keyboard.current?.hKey.wasPressedThisFrame==true)TeachingHints.Dismiss(now);
        }
        void HUD()
        {
            if(Game.Panel!="")return;
            // Strict whitelist: shift, time, health, stamina. No focus, reticle, markers or notices.
            if(Event.current.type==EventType.Repaint)HudElementCountForTest=4;
            paperSurface=false;
            TextShadow(new Rect(1168,35,242,31),"СМЕНА  "+S.day.ToString("00"),bold,paper);
            int seconds=Mathf.CeilToInt(Mathf.Max(0,S.dayLength-S.time));
            string clock=S.phase=="summary"?"00:00":(seconds/60).ToString("00")+":"+(seconds%60).ToString("00");
            SegmentClock(new Vector2(1170,77),clock,1.0f,paper);
            var crew=Danger?HotelDangerRules.Crew(S,Game.Session.LocalId):null;
            Gauge(new Rect(78,790,247,21),Mathf.Clamp01((crew?.health??100f)/100f),new Color(.88f,.36f,.28f),true);
            Gauge(new Rect(78,839,207,13),Mathf.Clamp01(Game.Stamina.Amount/100f),new Color(.83f,.81f,.48f),false);
            if(TeachingHints.Visible)TeachingHint();
        }
        void Gauge(Rect rect,float value,Color color,bool health)
        {
            CutBox(new Rect(rect.x-3,rect.y-3,rect.width+6,rect.height+6),7,new Color(.06f,.055f,.045f,.9f));
            CutBox(new Rect(rect.x-1,rect.y-1,rect.width+2,rect.height+2),5,paper);
            CutBox(rect,4,new Color(.12f,.115f,.09f,.7f));
            int count=health?10:8;float gap=3, w=(rect.width-(count-1)*gap)/count;
            for(int i=0;i<count;i++) {
                float part=Mathf.Clamp01(value*count-i);
                if(part>0)TexturedCutBox(new Rect(rect.x+i*(w+gap),rect.y,w*part,rect.height),Mathf.Min(3,w*part*.25f),metalTexture,color*1.6f,70);
            }
            if(health) {
                CutBox(new Rect(40,rect.y-4,25,29),5,new Color(.09f,.07f,.06f,.9f));
                Box(new Rect(49,rect.y,7,21),paper);Box(new Rect(42,rect.y+7,21,7),paper);
            } else {
                Line(new Vector2(57,rect.y-6),new Vector2(45,rect.y+7),5,paper);
                Line(new Vector2(45,rect.y+7),new Vector2(59,rect.y+7),5,paper);
                Line(new Vector2(59,rect.y+7),new Vector2(48,rect.y+20),5,paper);
            }
        }
        void SegmentClock(Vector2 at,string text,float size,Color color)
        {
            int[] bits={63,6,91,79,102,109,125,7,127,111};float x=at.x;
            foreach(char c in text) {
                if(c==':') {Box(new Rect(x+3,at.y+13*size,4*size,4*size),color);Box(new Rect(x+3,at.y+31*size,4*size,4*size),color);x+=14*size;continue;}
                int mask=bits[c-'0'];Vector2[] p={new Vector2(4,0),new Vector2(26,4),new Vector2(26,28),new Vector2(4,49),new Vector2(0,28),new Vector2(0,4),new Vector2(4,24)};
                for(int i=0;i<7;i++)if((mask&(1<<i))!=0) {
                    bool horizontal=i==0||i==3||i==6;
                    Rect r=new Rect(x+p[i].x*size,at.y+p[i].y*size,(horizontal?20:4)*size,(horizontal?4:20)*size);
                    Box(new Rect(r.x+1,r.y+2,r.width,r.height),new Color(0,0,0,.8f));CutBox(r,1,color);
                }
                x+=36*size;
            }
        }
        void TeachingHint()
        {
            if(Event.current.type==EventType.Repaint)TeachingHintDrawnForTest=true;
            var r=new Rect(368,29,700,130);
            CutBox(new Rect(r.x+3,r.y+5,r.width,r.height),17,new Color(0,0,0,.25f));
            TexturedCutBox(r,17,screenTexture,new Color(.82f,.97f,.84f));Box(new Rect(r.x+18,r.y+18,4,r.height-36),stamp);
            paperSurface=true;
            Text(new Rect(r.x+37,r.y+12,531,25),TeachingHints.Title,bold,gold);
            Text(new Rect(r.x+37,r.y+44,560,75),TeachingHints.Body,small);
            Text(new Rect(r.x+602,r.y+20,82,45),"H\nскрыть",small,muted);
            Line(new Vector2(r.x+599,r.y+20),new Vector2(r.x+599,r.y+109),1,brass);
            paperSurface=false;
        }
        void TextShadow(Rect r,string value,GUIStyle style,Color color)
        {
            Text(new Rect(r.x+2,r.y+2,r.width,r.height),value,style,new Color(.03f,.03f,.02f,.95f));
            Text(r,value,style,color);
        }
        void TabletContext()
        {
            var alert=HotelHudModel.Alert(S,Game.Session.LocalId,true);
            if(alert.kind!="") {MvpSection(alert.title);MvpText(alert.detail);}
            if(!string.IsNullOrEmpty(Game.Session.SaveError)){MvpSection("НЕ УДАЛОСЬ СОХРАНИТЬ");MvpText(Game.Session.SaveError);}
            if(Fold("tablet-context","Ваше снаряжение, объект и отметки")) {
                MvpText(HotelPresentation.HeldLabel(S,Game.Held));
                if(lastFocus!="")MvpText("Последний объект перед открытием: "+lastFocus,small);
                var ping=S.mvp?.pings.LastOrDefault();
                MvpText(ping==null?"Отметок нет.":(ping.playerId==Game.Session.LocalId?"Ваша отметка: ":"Отметка коллеги: ")+MvpTargetName(ping.target));
                MvpText("E — взаимодействие в мире. Удерживайте для работы. Q — положить предмет. Закройте планшет перед действием.",small,muted);
            }
            if(Fold("tablet-notices","Уведомления службы · "+tabletNotices.Count)) {
                if(tabletNotices.Count==0)MvpText("Новых сообщений нет.",small,muted);
                foreach(string entry in tabletNotices)MvpText(entry,small);
            }
        }
        void TeachingControls()
        {
            if(MvpButton("Всплывающие подсказки: "+(TeachingHints.Enabled?"включены":"выключены"))) {
                TeachingHints.Enabled=!TeachingHints.Enabled;TeachingHints.Dismiss(Time.unscaledTime);
                if(!Game.Automated){PlayerPrefs.SetInt("localTeachingHints",TeachingHints.Enabled?1:0);PlayerPrefs.Save();}
            }
            MvpText("H скрывает текущую подсказку. Настройка только для вас; учебный темп отеля не меняется.",small,muted);
        }
    }
}
