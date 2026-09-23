using System.Collections.Generic;

namespace WorstHotel
{
    // Local presentation only: never changes the host's lesson progress or guided clock.
    public sealed class HotelTeachingHints
    {
        public const float Lifetime=8f, QuietTime=24f, InitialDelay=2f;
        public bool Enabled=true;
        public bool Visible { get; private set; }
        public string Title { get; private set; }="";
        public string Body { get; private set; }="";
        public string CurrentKey { get; private set; }="";
        readonly HashSet<string> seen=new HashSet<string>();
        float expires, next;
        public void Reset(float now) { seen.Clear(); Hide(); next=now+InitialDelay; }
        public void Tick(float now,string key,string title,string body,bool allowed)
        {
            if(!Enabled||!allowed) { if(Visible)Dismiss(now); return; }
            if(Visible&&now>=expires) { Hide(); next=now+QuietTime; }
            if(Visible||now<next||string.IsNullOrEmpty(key)||seen.Contains(key))return;
            if(seen.Count>=256)return;
            seen.Add(key);CurrentKey=key;Title=title??"";Body=body??"";
            Visible=true;expires=now+Lifetime;
        }
        public void Dismiss(float now) { Hide(); next=now+QuietTime; }
        void Hide() { Visible=false;Title="";Body="";CurrentKey=""; }
    }
}
