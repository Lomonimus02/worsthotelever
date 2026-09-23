using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class HotelUI
    {
        readonly Color documentInk=new Color(.105f,.18f,.15f),documentMuted=new Color(.28f,.36f,.28f);
        readonly Color documentPaper=new Color(.80f,.85f,.70f),documentShade=new Color(.69f,.76f,.61f);
        readonly Color stamp=new Color(.37f,.14f,.14f),brass=new Color(.58f,.49f,.30f);
        readonly Dictionary<string,bool> folds=new Dictionary<string,bool>();
        readonly List<Texture2D> uiTextures=new List<Texture2D>();
        Texture2D[] corners;
        readonly Dictionary<string,Rect> buttonRects=new Dictionary<string,Rect>();
        public IReadOnlyDictionary<string,Rect> ButtonRectsForTest=>buttonRects;
        Texture2D Solid(Color color)
        {
            var texture=new Texture2D(1,1,TextureFormat.RGBA32,false){hideFlags=HideFlags.HideAndDontSave};
            texture.SetPixel(0,0,color);texture.Apply();uiTextures.Add(texture);return texture;
        }
        void InitTheme()
        {
            corners=new Texture2D[4];
            for(int k=0;k<4;k++) {
                var t=new Texture2D(32,32,TextureFormat.RGBA32,false){hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Bilinear};
                for(int y=0;y<32;y++)for(int x=0;x<32;x++) {
                    int px=(k==0||k==2)?x:31-x,py=k<2?31-y:y;
                    t.SetPixel(x,y,px+py>=31?Color.white:Color.clear);
                }
                t.Apply();uiTextures.Add(t);corners[k]=t;
            }
            var input=GUI.skin.textField;
            input.normal.background=Solid(new Color(.89f,.92f,.80f));
            input.hover.background=input.normal.background;input.focused.background=input.normal.background;
            input.normal.textColor=documentInk;input.hover.textColor=documentInk;input.focused.textColor=documentInk;
            GUI.skin.settings.cursorColor=stamp;GUI.skin.settings.selectionColor=documentShade;
            GUI.skin.verticalScrollbar.normal.background=Solid(documentShade);GUI.skin.verticalScrollbar.fixedWidth=14;
            var thumb=GUI.skin.verticalScrollbarThumb;
            thumb.normal.background=Solid(documentMuted);thumb.hover.background=thumb.normal.background;thumb.active.background=thumb.normal.background;
            thumb.border=new RectOffset(2,2,2,2);
            GUI.skin.horizontalSlider.normal.background=Solid(documentMuted);
            GUI.skin.horizontalSliderThumb.normal.background=Solid(stamp);
            GUI.skin.horizontalSliderThumb.hover.background=GUI.skin.horizontalSliderThumb.normal.background;
            GUI.skin.horizontalSliderThumb.active.background=GUI.skin.horizontalSliderThumb.normal.background;
        }
        void OnDestroy(){foreach(var texture in uiTextures)if(texture!=null)Destroy(texture);}
        void CutBox(Rect r,float cut,Color color)
        {
            if(r.width<=0||r.height<=0)return;
            cut=Mathf.Clamp(cut,0,Mathf.Min(r.width,r.height)*.5f);
            if(cut<.5f||corners==null){Box(r,color);return;}
            Box(new Rect(r.x+cut,r.y,r.width-2*cut,r.height),color);
            Box(new Rect(r.x,r.y+cut,cut,r.height-2*cut),color);
            Box(new Rect(r.xMax-cut,r.y+cut,cut,r.height-2*cut),color);
            Color old=GUI.color;GUI.color=color;
            GUI.DrawTexture(new Rect(r.x,r.y,cut,cut),corners[0]);GUI.DrawTexture(new Rect(r.xMax-cut,r.y,cut,cut),corners[1]);
            GUI.DrawTexture(new Rect(r.x,r.yMax-cut,cut,cut),corners[2]);GUI.DrawTexture(new Rect(r.xMax-cut,r.yMax-cut,cut,cut),corners[3]);
            GUI.color=old;
        }
        void Line(Vector2 a,Vector2 b,float width,Color color)
        {
            Matrix4x4 saved=GUI.matrix;
            GUI.matrix=saved*Matrix4x4.TRS(new Vector3(a.x,a.y,0),Quaternion.Euler(0,0,Mathf.Atan2(b.y-a.y,b.x-a.x)*Mathf.Rad2Deg),Vector3.one);
            Box(new Rect(0,-width*.5f,(b-a).magnitude,width),color);GUI.matrix=saved;
        }
        Color ThemeText(Color requested)
        {
            if(!paperSurface)return requested;
            if(requested==gold||requested==red)return stamp;
            if(requested==muted)return documentMuted;
            if(requested==green)return new Color(.12f,.32f,.22f);
            return documentInk;
        }
        void DrawButtonSurface(Rect r,bool enabled,bool accent,bool hover)
        {
            Color fill=accent?new Color(.18f,.30f,.24f):hover?new Color(.61f,.69f,.52f):documentShade;
            if(!paperSurface)fill=hover?new Color(.38f,.25f,.22f):new Color(.16f,.16f,.14f);
            if(!enabled)fill=new Color(.74f,.79f,.66f);
            CutBox(new Rect(r.x,r.y+2,r.width,r.height),6,new Color(.12f,.18f,.13f,.35f));
            CutBox(r,6,fill);
            if(accent&&enabled)Box(new Rect(r.x+4,r.y+7,3,r.height-14),new Color(.86f,.69f,.39f));
            else Box(new Rect(r.x+7,r.yMax-2,r.width-14,1),new Color(.23f,.35f,.24f,.6f));
        }
        void Screw(float x,float y)
        {
            CutBox(new Rect(x-6,y-6,12,12),4,new Color(.52f,.48f,.36f));
            Line(new Vector2(x-3,y-2),new Vector2(x+3,y+2),2,new Color(.10f,.10f,.085f));
        }
        void DrawDocument(Rect r)
        {
            if(Event.current.type==EventType.Repaint)TabletDrawnForTest=true;
            paperSurface=false;
            Box(new Rect(0,0,1440,900),new Color(.035f,.035f,.025f,.35f));
            // Protective bumpers, molded body, inset LCD, grille and physical home key.
            CutBox(new Rect(111,91,1232,760),40,new Color(0,0,0,.35f));
            CutBox(new Rect(95,66,1250,776),40,new Color(.07f,.075f,.07f));
            CutBox(new Rect(106,75,1228,755),32,new Color(.44f,.22f,.20f));
            CutBox(new Rect(113,83,1214,736),29,new Color(.29f,.115f,.12f));
            foreach(float x in new[]{113f,1264f}) {
                CutBox(new Rect(x,86,66,57),13,new Color(.115f,.115f,.105f));
                CutBox(new Rect(x,756,66,54),13,new Color(.115f,.115f,.105f));
            }
            Text(new Rect(188,92,337,23),"GRAND / SERVICE SYSTEM",small,paper);
            for(int i=0;i<9;i++)Box(new Rect(663+i*13,100,7,3),new Color(.06f,.055f,.05f));
            CutBox(new Rect(1210,98,9,9),3,new Color(.58f,.81f,.49f));
            Text(new Rect(1112,90,94,24),"STAFF-01",small,paper);
            CutBox(new Rect(r.x-14,r.y-13,r.width+28,r.height+26),12,new Color(.10f,.12f,.10f));
            CutBox(new Rect(r.x-5,r.y-5,r.width+10,r.height+10),7,new Color(.40f,.44f,.34f));
            Box(r,documentPaper);
            for(int y=0;y<(int)r.height;y+=6)Box(new Rect(r.x,r.y+y,r.width,1),new Color(.12f,.23f,.14f,.022f));
            Screw(138,112);Screw(1303,112);Screw(138,782);Screw(1303,782);
            for(int j=0;j<5;j++)Box(new Rect(1304,370+j*17,8,9),new Color(.09f,.075f,.065f));
            // Side grip moulding and small worn edges remain outside the touchscreen.
            Line(new Vector2(124,166),new Vector2(124,330),2,new Color(.64f,.40f,.32f,.65f));
            Line(new Vector2(1123,811),new Vector2(1240,811),2,new Color(.64f,.40f,.32f,.6f));
            if(Button(new Rect(669,794,102,29),"TAB"))Game.OpenPanel(Game.Playing?"":"menu");
            Text(new Rect(200,792,285,29),"СЛУЖЕБНЫЙ · НЕ ВЫНОСИТЬ",small,muted);
            paperSurface=true;
        }
        bool Fold(string id,string label)
        {
            bool open=folds.TryGetValue(id,out bool value)&&value;
            if(MvpButton((open?"−  ":"+  ")+label)) {open=!open;folds[id]=open;}
            return open;
        }
        void JournalHintBody(string body)
        {
            MvpText(body);
            if(Event.current.type==EventType.Repaint)JournalTutorialBodyForTest=body;
        }
    }
}
