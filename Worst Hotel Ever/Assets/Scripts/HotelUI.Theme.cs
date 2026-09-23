using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class HotelUI
    {
        readonly Color documentInk=new Color(.86f,.92f,.77f),documentMuted=new Color(.73f,.80f,.69f);
        readonly Color documentPaper=new Color(.063f,.09f,.075f),documentShade=new Color(.14f,.20f,.16f);
        readonly Color stamp=new Color(.98f,.83f,.56f),brass=new Color(.44f,.48f,.33f);
        readonly Dictionary<string,bool> folds=new Dictionary<string,bool>();
        readonly List<Texture2D> uiTextures=new List<Texture2D>();
        Texture2D[] corners;
        Texture2D casingTexture,screenTexture,rubberTexture,metalTexture;
        readonly Dictionary<string,Rect> buttonRects=new Dictionary<string,Rect>();
        public IReadOnlyDictionary<string,Rect> ButtonRectsForTest=>buttonRects;
        void InitTheme()
        {
            casingTexture=HotelTextureLibrary.GetTexture(HotelTextureId.TabletCasing);
            screenTexture=HotelTextureLibrary.GetTexture(HotelTextureId.TabletScreen);
            rubberTexture=HotelTextureLibrary.GetTexture(HotelTextureId.Rubber);
            metalTexture=HotelTextureLibrary.GetTexture(HotelTextureId.Metal);
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
            input.normal.background=screenTexture;
            input.hover.background=input.normal.background;input.focused.background=input.normal.background;
            input.normal.textColor=documentInk;input.hover.textColor=documentInk;input.focused.textColor=documentInk;
            GUI.skin.settings.cursorColor=stamp;GUI.skin.settings.selectionColor=documentShade;
            GUI.skin.verticalScrollbar.normal.background=screenTexture;GUI.skin.verticalScrollbar.fixedWidth=14;
            var thumb=GUI.skin.verticalScrollbarThumb;
            thumb.normal.background=metalTexture;thumb.hover.background=thumb.normal.background;thumb.active.background=thumb.normal.background;
            thumb.border=new RectOffset(2,2,2,2);
            GUI.skin.horizontalSlider.normal.background=rubberTexture;
            GUI.skin.horizontalSliderThumb.normal.background=metalTexture;
            GUI.skin.horizontalSliderThumb.hover.background=GUI.skin.horizontalSliderThumb.normal.background;
            GUI.skin.horizontalSliderThumb.active.background=GUI.skin.horizontalSliderThumb.normal.background;
        }
        void OnDestroy(){foreach(var texture in uiTextures)if(texture!=null)Destroy(texture);}
        // Textures are shared immutable Resources assets, never owned/destroyed by this component.
        // Pixel-height strips texture the entire chamfer, including the corner triangles.
        // Continuous UVs avoid solid corner patches; this only runs while a widget is drawn.
        void TexturedCutBox(Rect r,float cut,Texture2D texture,Color tint,float tilePixels=0)
        {
            if(r.width<=0||r.height<=0)return;
            cut=Mathf.Clamp(cut,0,Mathf.Min(r.width,r.height)*.5f);
            if(texture==null){CutBox(r,cut,tint);return;}
            void Strip(Rect part)
            {
                if(part.width<=0||part.height<=0)return;
                float ux=tilePixels>0?r.width/tilePixels:1,uy=tilePixels>0?r.height/tilePixels:1;
                var uv=new Rect((part.x-r.x)/r.width*ux,(1-(part.yMax-r.y)/r.height)*uy,part.width/r.width*ux,part.height/r.height*uy);
                Color old=GUI.color;GUI.color=tint;GUI.DrawTextureWithTexCoords(part,texture,uv);GUI.color=old;
            }
            Strip(new Rect(r.x,r.y+cut,r.width,r.height-2*cut));
            int rows=Mathf.CeilToInt(cut*Mathf.Clamp(Mathf.Abs(GUI.matrix.m11),.5f,2));
            for(int i=0;i<rows;i++) {
                float y=cut*i/rows,h=cut/rows,inset=cut-y-h*.5f;
                Strip(new Rect(r.x+inset,r.y+y,r.width-2*inset,h));
                Strip(new Rect(r.x+inset,r.yMax-y-h,r.width-2*inset,h));
            }
        }
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
            if(requested==gold)return stamp;
            if(requested==red)return new Color(.98f,.53f,.40f);
            if(requested==muted)return documentMuted;
            if(requested==green)return new Color(.58f,.84f,.63f);
            return documentInk;
        }
        void DrawButtonSurface(Rect r,bool enabled,bool accent,bool hover)
        {
            Color fill=accent?new Color(.43f,.53f,.34f):hover?new Color(.37f,.49f,.39f):new Color(.23f,.34f,.27f);
            if(!paperSurface)fill=hover?new Color(.44f,.40f,.29f):new Color(.29f,.29f,.24f);
            if(!enabled)fill=new Color(.17f,.23f,.19f);
            CutBox(new Rect(r.x,r.y+3,r.width,r.height),6,new Color(.015f,.02f,.018f,.8f));
            TexturedCutBox(r,6,metalTexture,fill,180);
            if(accent&&enabled)Box(new Rect(r.x+4,r.y+7,3,r.height-14),new Color(.86f,.69f,.39f));
            else Box(new Rect(r.x+7,r.yMax-2,r.width-14,1),new Color(.42f,.52f,.38f,.5f));
        }
        void Screw(float x,float y)
        {
            TexturedCutBox(new Rect(x-6,y-6,12,12),4,metalTexture,new Color(.73f,.69f,.53f),32);
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
            TexturedCutBox(new Rect(106,75,1228,755),32,metalTexture,new Color(.51f,.43f,.33f),320);
            TexturedCutBox(new Rect(113,83,1214,736),29,casingTexture,new Color(.87f,.83f,.78f),600);
            foreach(float x in new[]{113f,1264f}) {
                TexturedCutBox(new Rect(x,86,66,57),13,rubberTexture,new Color(.50f,.51f,.46f),85);
                TexturedCutBox(new Rect(x,756,66,54),13,rubberTexture,new Color(.50f,.51f,.46f),85);
            }
            Text(new Rect(188,92,337,23),"GRAND / SERVICE SYSTEM",small,paper);
            for(int i=0;i<9;i++)Box(new Rect(663+i*13,100,7,3),new Color(.06f,.055f,.05f));
            CutBox(new Rect(1210,98,9,9),3,new Color(.58f,.81f,.49f));
            Text(new Rect(1112,90,94,24),"STAFF-01",small,paper);
            CutBox(new Rect(r.x-14,r.y-13,r.width+28,r.height+26),12,new Color(.10f,.12f,.10f));
            TexturedCutBox(new Rect(r.x-5,r.y-5,r.width+10,r.height+10),7,rubberTexture,new Color(.60f,.67f,.53f),160);
            TexturedCutBox(r,0,screenTexture,new Color(.80f,.95f,.83f));
            for(int y=0;y<(int)r.height;y+=6)Box(new Rect(r.x,r.y+y,r.width,1),new Color(.36f,.49f,.31f,.035f));
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
