using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class HotelUI
    {
        readonly Color documentInk=new Color(.19f,.16f,.13f), documentMuted=new Color(.39f,.35f,.28f);
        readonly Color documentPaper=new Color(.95f,.90f,.77f), documentShade=new Color(.87f,.81f,.66f);
        readonly Color stamp=new Color(.46f,.12f,.12f), brass=new Color(.66f,.46f,.22f);
        readonly Dictionary<string,bool> folds=new Dictionary<string,bool>();
        readonly List<Texture2D> uiTextures=new List<Texture2D>();

        Texture2D Solid(Color color)
        {
            var texture=new Texture2D(1,1,TextureFormat.RGBA32,false){hideFlags=HideFlags.HideAndDontSave};
            texture.SetPixel(0,0,color);texture.Apply();uiTextures.Add(texture);return texture;
        }
        void InitTheme()
        {
            var input=GUI.skin.textField;
            input.normal.background=Solid(new Color(.99f,.96f,.86f));
            input.hover.background=input.normal.background;input.focused.background=input.normal.background;
            input.normal.textColor=documentInk;input.hover.textColor=documentInk;input.focused.textColor=documentInk;
            GUI.skin.settings.cursorColor=stamp;GUI.skin.settings.selectionColor=new Color(.68f,.60f,.44f);
            GUI.skin.verticalScrollbar.normal.background=Solid(documentShade);
            GUI.skin.verticalScrollbar.fixedWidth=14;
            var thumb=GUI.skin.verticalScrollbarThumb;
            thumb.normal.background=Solid(brass);thumb.hover.background=thumb.normal.background;thumb.active.background=thumb.normal.background;
            thumb.border=new RectOffset(2,2,2,2);
        }
        void OnDestroy(){foreach(var texture in uiTextures)if(texture!=null)Destroy(texture);}
        Color ThemeText(Color requested)
        {
            if(!paperSurface)return requested;
            if(requested==gold||requested==red)return stamp;
            if(requested==muted)return documentMuted;
            if(requested==green)return new Color(.16f,.35f,.25f);
            return documentInk;
        }
        void DrawButtonSurface(Rect r,bool enabled,bool accent,bool hover)
        {
            Color fill=paperSurface?(accent?stamp:hover?new Color(.82f,.75f,.58f):documentShade):
                accent?stamp:hover?new Color(.24f,.31f,.29f):ink;
            if(!enabled)fill=paperSurface?new Color(.88f,.84f,.74f):new Color(.21f,.25f,.24f);
            Box(new Rect(r.x,r.y+2,r.width,r.height),paperSurface?new Color(.36f,.29f,.20f,.25f):new Color(0,0,0,.3f));
            Box(r,fill);
            Box(new Rect(r.x,r.y+r.height-2,r.width,2),enabled?brass:new Color(.6f,.57f,.49f,.35f));
        }
        void DrawDocument(Rect r)
        {
            paperSurface=false;
            Box(new Rect(0,0,1440,900),new Color(.05f,.035f,.025f,.46f));
            Box(new Rect(r.x+11,r.y+12,r.width,r.height),new Color(0,0,0,.27f));
            Box(new Rect(r.x-8,r.y-8,r.width+16,r.height+16),new Color(.27f,.19f,.12f));
            Box(r,documentPaper);
            Box(new Rect(r.x,r.y,12,r.height),stamp);
            Box(new Rect(r.x+27,r.y+107,r.width-57,1),brass);
            // Stationery details drawn in code: no downloaded art or font dependencies.
            Box(new Rect(r.x+r.width*.5f-54,r.y-17,108,23),brass);
            Box(new Rect(r.x+r.width*.5f-37,r.y-11,74,9),documentInk);
            for(int i=0;i<4;i++)Box(new Rect(r.x+r.width-24,r.y+38+i*9,10+i%2*6,1),new Color(.46f,.34f,.2f,.2f));
            paperSurface=true;
        }
        void DrawMenuPaper()
        {
            paperSurface=false;
            Box(new Rect(18,0,559,900),new Color(0,0,0,.24f));
            Box(new Rect(0,0,565,900),documentPaper);
            Box(new Rect(0,0,19,900),stamp);
            Box(new Rect(37,38,490,1),brass);Box(new Rect(37,819,490,1),brass);
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
