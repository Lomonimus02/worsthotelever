using System;
using System.Linq;
using UnityEngine;

namespace WorstHotel
{
    // Resolution-independent UI, built in code so a clean checkout needs no manual scene wiring.
    public sealed class HotelUI : MonoBehaviour
    {
        public HotelGame Game;
        HotelState S=>Game.Session.State;
        GUIStyle small,normal,bold,title,huge,button;
        readonly Color ink=new Color(.095f,.135f,.14f), paper=new Color(.96f,.93f,.85f), muted=new Color(.82f,.86f,.81f), red=new Color(.68f,.24f,.22f), gold=new Color(.9f,.68f,.36f), green=new Color(.35f,.71f,.53f);
        string address="127.0.0.1",port="7777",password="",steamLobby="";
        bool hostLoad;
        Vector2 scroll, guestScroll; string lastPhase="";
        int selectedGuest;
        void Init()
        {
            Font font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            small=new GUIStyle(GUI.skin.label){font=font,fontSize=15,wordWrap=true,richText=false};small.normal.textColor=paper;
            normal=new GUIStyle(small){fontSize=19};
            bold=new GUIStyle(normal){fontStyle=FontStyle.Bold};
            title=new GUIStyle(bold){fontSize=32};
            huge=new GUIStyle(bold){fontSize=70};
            button=new GUIStyle(bold){alignment=TextAnchor.MiddleCenter,wordWrap=true,fontSize=18};button.normal.textColor=paper;
            GUI.skin.textField.font=font;GUI.skin.textField.fontSize=22;
            GUI.skin.textField.padding=new RectOffset(12,12,10,10);
            GUI.skin.horizontalSlider.fixedHeight=18; GUI.skin.horizontalSliderThumb.fixedWidth=20;GUI.skin.horizontalSliderThumb.fixedHeight=22;
        }
        void OnGUI()
        {
            if(Game==null||Game.View==null)return;if(normal==null)Init();
            float scale=Mathf.Min(Screen.width/1440f,Screen.height/900f);
            GUI.matrix=Matrix4x4.TRS(new Vector3((Screen.width-1440*scale)*.5f,(Screen.height-900*scale)*.5f,0),Quaternion.identity,Vector3.one*scale);
            if(Game.Playing) {
                if(S.phase!=lastPhase){lastPhase=S.phase;if(S.phase=="summary")Game.OpenPanel("summary");}
                HUD();
            }
            if(Game.Panel!="") {
                if(Game.Panel=="menu"||Game.Panel=="connect"||Game.Panel=="new")Menu();
                else if(Game.Panel=="settings") Settings();
                else if(Game.Panel=="pause") Pause();
                else if(Game.Panel=="steam") SteamMenu();
                else if(Game.Panel=="recovery") Recovery();
                else if(Game.Playing) Window();
                else if(!Game.Session.Connecting)Game.Panel="menu";
            }
            if(Game.Session.Connecting&&!Game.Playing) {
                Box(new Rect(430,326,580,245),ink);Text(new Rect(459,352,520,60),"Подключаемся к отелю…",title);
                Text(new Rect(459,424,520,50),Game.Session.Status,normal);
                if(Button(new Rect(459,498,520,47),"Отменить")){Game.Session.Disconnect(false);Game.OpenPanel("menu");}
            }
            if(Game.Toast!=""&&(Game.Playing||Game.Panel=="steam")) {
                float toastY=Game.Panel==""?115:800;
                float toastHeight=Mathf.Clamp(normal.CalcHeight(new GUIContent(Game.Toast),662)+22,60,96);
                Box(new Rect(370,toastY,700,toastHeight),new Color(.07f,.12f,.13f,.94f));Text(new Rect(389,toastY+11,662,toastHeight-18),Game.Toast,normal);
            }
            GUI.matrix=Matrix4x4.identity;
        }
        void Box(Rect rect,Color color) {Color old=GUI.color;GUI.color=color;GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=old;}
        void Text(Rect rect,string text,GUIStyle style=null,Color? color=null) {
            var s=style??normal;Color old=s.normal.textColor;if(color.HasValue)s.normal.textColor=color.Value;
            GUI.Label(rect,text,s);s.normal.textColor=old;
        }
        bool Button(Rect r,string label,bool enabled=true,bool accent=false)
        {
            bool hover=r.Contains(Event.current.mousePosition);
            Box(r,!enabled?new Color(.19f,.23f,.24f):accent?(hover?new Color(.82f,.35f,.26f):red):(hover?new Color(.23f,.33f,.32f):new Color(.16f,.24f,.24f)));
            bool old=GUI.enabled;GUI.enabled=enabled;
            bool pressed=GUI.Button(r,label,button);GUI.enabled=old;return pressed;
        }
        void Menu()
        {
            Box(new Rect(0,0,565,900),new Color(.055f,.10f,.11f,.95f));
            Box(new Rect(49,67,62,5),gold);Text(new Rect(49,90,450,30),"КОМАНДА НУЖНА. ОПЫТ НЕОБЯЗАТЕЛЕН.",small,gold);
            Text(new Rect(43,140,510,260),"WORST\nHOTEL\nEVER",huge);
            Text(new Rect(49,388,465,78),"Четыре номера. Два сотрудника.\nИ всё обязательно пойдёт не так.",normal);
            Text(new Rect(49,837,465,30),"PRE-MVP 0.4  /  WINDOWS  /  1–2 СОТРУДНИКА",small,muted);
            Box(new Rect(998,24,397,74),new Color(.065f,.105f,.11f,.84f));
            Text(new Rect(1017,36,366,50),"ОТЕЛЬ «ПОЧТИ ГРАНД»\n★  НАЧНИТЕ С ЧИСТОГО ПОЛОТЕНЦА",small,paper);
            if(Game.Panel=="menu" && Button(new Rect(980,673,405,53),"Тест Steam (AppID 480)"))Game.Panel="steam";
            if(Game.Panel=="connect") {
                Text(new Rect(50,480,470,40),"Подключение к другу",title);
                Text(new Rect(50,531,310,25),"IP-адрес",small);address=GUI.TextField(new Rect(50,559,300,48),address,100);
                Text(new Rect(370,531,140,25),"UDP-порт",small);port=GUI.TextField(new Rect(370,559,140,48),port,5);
                Text(new Rect(50,620,470,25),"Пароль комнаты (если задан)",small);password=GUI.PasswordField(new Rect(50,648,460,44),password,'●',32);
                if(Button(new Rect(50,710,280,53),Game.Session.Connecting?"Подключаемся…":"Присоединиться",!Game.Session.Connecting,true)) {
                    if(!ushort.TryParse(port,out ushort value)||value==0)Game.Session.Status="Неверный порт.";
                    else {Game.Session.Disconnect(false);Game.Session.Join(address,value,password);Game.OpenPanel("");}
                }
                if(Button(new Rect(345,710,165,53),"Назад")){Game.Session.Disconnect(false);Game.Panel="menu";}
                Text(new Rect(50,775,460,55),"Локальная сеть / VPN-сеть / доступный IP.\nДля прямого интернета хосту нужен UDP-порт.",small,muted);
            } else if(Game.Panel=="new") {
                Text(new Rect(50,473,460,80),hostLoad?"Продолжить сохранённый отель. Правила начатой истории сохранятся.":"Новая история: спокойное обучение.\nПрежний отель сохранится в архиве.",normal);
                Text(new Rect(50,559,305,24),"Пароль комнаты (необязательно)",small);
                Text(new Rect(370,559,140,24),"UDP-порт",small);
                password=GUI.PasswordField(new Rect(50,590,300,45),password,'●',32);port=GUI.TextField(new Rect(370,590,140,45),port,5);
                if(Button(new Rect(50,658,460,57),"Открыть отель для друзей",true,true)) {
                    if(!ushort.TryParse(port,out ushort value)||value==0)Game.Session.Status="Неверный порт.";
                    else{Game.Session.Disconnect(false);Game.Session.Host(hostLoad,value,password);Game.OpenPanel("");}
                }
                if(Button(new Rect(50,732,460,48),"Вернуться"))Game.Panel="menu";
            } else {
                bool saved=System.IO.File.Exists(Game.Session.SavePath)||System.IO.File.Exists(Game.Session.SavePath+".bak");
                if(Button(new Rect(50,493,460,58),"Продолжить отель",saved,true)) {hostLoad=true;Game.Panel="new";}
                if(Button(new Rect(50,564,460,58),"Создать новый отель")) {
                    hostLoad=false;Game.Panel="new";
                }
                if(Button(new Rect(50,635,460,58),"Присоединиться к другу"))Game.Panel="connect";
                if(Button(new Rect(50,706,224,49),"Настройки"))Game.Panel="settings";
                if(Button(new Rect(286,706,224,49),"Выйти"))Application.Quit();
                Text(new Rect(50,776,460,48),"WASD — ходить · E — работать · Q — положить",small,muted);
            }
            if(!string.IsNullOrEmpty(Game.Session.Status)) {Box(new Rect(595,745,790,100),new Color(.065f,.105f,.11f,.95f));Text(new Rect(615,760,750,72),Game.Session.Status,normal);}
        }
        void HUD()
        {
            Box(new Rect(25,22,1390,76),new Color(.065f,.1f,.11f,.93f));Box(new Rect(25,22,6,76),gold);
            Text(new Rect(48,35,260,27),"ПОЧТИ ГРАНД",bold);Text(new Rect(48,64,320,22),Game.Session.IsHost?"ВЫ — ХОСТ  ·  СОТРУДНИКОВ "+S.players.Count+"/2":"КООПЕРАТИВ  ·  СОТРУДНИКОВ "+S.players.Count+"/2",small,muted);
            Text(new Rect(367,36,300,32),"ДЕНЬ "+S.day+"  /  "+Phase(S.phase),bold);
            float left=Mathf.Max(0,S.dayLength-S.time);
            Text(new Rect(730,36,315,44),HotelDirector.ClockHeld(S)?"УЧЕБНЫЙ ТАЙМЕР ОСТАНОВЛЕН":S.phase=="open"?"ДО ЗАКРЫТИЯ  "+((int)left/60).ToString("00")+":"+((int)left%60).ToString("00"):"ВЫДОХНИТЕ. ПОКА.",small,gold);
            Text(new Rect(1080,34,290,38),S.cash+" ₽",title,green);
            if(Game.Panel!="")return;
            Box(new Rect(716,446,8,8),paper);
            if(Game.FocusLabel!="") {
                Box(new Rect(365,662,710,76),new Color(.065f,.105f,.11f,.94f));
                Box(new Rect(380,684,32,32),gold);Text(new Rect(387,685,28,30),"E",bold,ink);
                Text(new Rect(430,674,625,60),Game.FocusLabel,normal);
            }
            if(Game.LocalPlayer!=null && Game.LocalPlayer.workTarget!="") {
                Box(new Rect(530,742,380,7),new Color(.2f,.28f,.27f));
                Box(new Rect(530,742,380*Mathf.Clamp01(Game.LocalPlayer.workProgress),7),gold);
                Text(new Rect(580,756,400,25),"Удерживайте E до завершения",small);
            }
            Box(new Rect(25,797,730,77),new Color(.065f,.105f,.11f,.90f));
            Text(new Rect(42,805,698,39),Game.Held==null?"Руки свободны. Самое время помочь коллеге.":HotelPresentation.HeldLabel(S,Game.Held)+"   ·   Q — положить",normal);
            Text(new Rect(42,845,698,23),"WASD — ходить   SHIFT — быстрее   TAB — задачи   ESC — меню",small,muted);
            Box(new Rect(1124,820,290,59),new Color(.065f,.105f,.11f,.90f));
            Text(new Rect(1140,832,260,42),"Автосохранение: "+(Game.Session.IsHost?Game.Session.LastSaved:"у хоста")+"\n"+(int)Game.FPS+" FPS",small,muted);
            var hint=HotelOnboarding.GetHint(S,Game.Session.LocalId);
            if(hint!=null) {
                Box(new Rect(25,125,330,246),new Color(.065f,.105f,.11f,.92f));
                Text(new Rect(42,140,296,24),(S.day==1?"ПЕРВАЯ СМЕНА":"ДЕНЬ 2 · ПРАКТИКА")+"  ·  "+hint.completed+" / "+hint.total,small,gold);
                Text(new Rect(42,175,296,58),hint.title,bold);
                Text(new Rect(42,239,296,114),hint.body,small);
                DrawHintTarget(hint.targetId);
            } else {
                var task=TaskLines().FirstOrDefault();
                if(task!=null){Box(new Rect(25,125,320,112),new Color(.065f,.105f,.11f,.87f));Text(new Rect(40,137,290,22),"ОБЩИЕ ЗАДАЧИ  ·  TAB",small,gold);Text(new Rect(40,165,290,62),task,normal);}
            }
            if(HotelDirector.IsGuided(S)) {
                Box(new Rect(25,441,330,123),new Color(.065f,.105f,.11f,.92f));
                Text(new Rect(42,452,296,77),HotelDirector.Status(S),small,gold);
                Text(new Rect(42,532,296,23),"ESC → План смены / темп",small,muted);
            }
        }
        void DrawHintTarget(string targetId)
        {
            if(!HotelPresentation.TryTarget(S,targetId,out Vector3 target))return;
            Vector3 delta=target-Game.View.transform.position;delta.y=0;
            Vector3 forward=Game.View.transform.forward;forward.y=0;
            float angle=Vector3.SignedAngle(forward,delta,Vector3.up);
            string direction=Mathf.Abs(angle)>135?"ПОЗАДИ":angle>30?"СПРАВА":angle< -30?"СЛЕВА":"ВПЕРЕДИ";
            Box(new Rect(25,379,330,49),new Color(.065f,.105f,.11f,.92f));
            Text(new Rect(42,391,298,30),direction+"  ·  "+Mathf.CeilToInt(delta.magnitude)+" м до цели",small,gold);
            Vector3 viewport=Game.View.WorldToViewportPoint(target+Vector3.up*.25f);
            if(viewport.z<=0||viewport.x<.27f||viewport.x>.92f||viewport.y<.29f||viewport.y>.77f)return;
            // Map actual camera pixels into this UI's letterboxed 1440x900 coordinate space.
            Vector3 pixel=Game.View.ViewportToScreenPoint(viewport);
            float scale=Mathf.Min(Screen.width/1440f,Screen.height/900f);
            float x=(pixel.x-(Screen.width-1440*scale)*.5f)/scale;
            float y=(Screen.height-pixel.y-(Screen.height-900*scale)*.5f)/scale;
            Box(new Rect(x-6,y-6,12,12),gold);Box(new Rect(x-3,y-3,6,6),ink);
        }
        void Frame(string heading,string sub)
        {
            Box(new Rect(0,0,1440,900),new Color(0,0,0,.48f));Box(new Rect(170,136,1100,645),ink);
            Box(new Rect(170,136,1100,5),gold);Text(new Rect(202,162,970,45),heading,title);
            Text(new Rect(204,212,970,35),sub,small,muted);
            if(Button(new Rect(1190,158,49,44),"×"))Game.OpenPanel(Game.Playing?"":"menu");
        }
        void Window()
        {
            string panel=Game.Panel;
            Frame(panel=="tasks"?"Работа найдётся каждому":panel=="reception"?"Ресепшен":panel=="summary"?"Смена окончена":panel=="briefing"?"План смены":panel=="pace-confirm"?"Перейти к обычному темпу?":"Управление отелем", "Мир продолжает жить, пока открыта панель.  /  ESC — вернуться в игру");
            if(panel=="summary") { Summary(); return; }
            if(panel=="finish-confirm") { FinishConfirmation(); return; }
            if(panel=="guest") { GuestDetails(); return; }
            if(panel=="pace-confirm") { PaceConfirmation(); return; }
            if(Button(new Rect(202,258,220,42),"Номера / гости"))Game.Panel="reception";
            if(Button(new Rect(434,258,220,42),"Задачи"))Game.Panel="tasks";
            if(Button(new Rect(666,258,220,42),"Смена / улучшения"))Game.Panel="management";
            if(Button(new Rect(898,258,340,42),"План смены / учебный темп"))Game.Panel="briefing";
            Box(new Rect(202,314,1036,2),new Color(.24f,.31f,.29f));
            if(panel=="tasks") { Tasks(); return; }
            if(panel=="management") { Management(); return; }
            if(panel=="briefing") { Briefing(); return; }
            Rooms();
        }
        void Briefing()
        {
            Text(new Rect(215,338,970,62),HotelDirector.Status(S),bold,gold);
            Text(new Rect(215,411,582,191),HotelDirector.DayBrief(S),normal);
            Text(new Rect(833,411,368,187),"ПОДСКАЗКИ И ТЕМП — РАЗНОЕ\n\nПодсказки объясняют действие. Учебный темп ограничивает нагрузку и даёт время освоиться.",normal,muted);
            bool helpAvailable=S.day==1||(S.contentVersion==1&&S.day==2);
            if(helpAvailable&&Button(new Rect(215,628,475,48),S.tutorialSkipped?"Показать подсказки":"Скрыть подсказки",Game.Session.IsHost))Send(S.tutorialSkipped?"resumeTutorial":"skipTutorial");
            if(!helpAvailable)Text(new Rect(215,633,475,44),"Подсказки доступны в начале истории.",small,muted);
            if(Button(new Rect(712,628,489,48),HotelDirector.IsGuided(S)?"Перейти к обычному темпу…":"Обычный темп",Game.Session.IsHost&&HotelDirector.IsGuided(S)))Game.Panel="pace-confirm";
            Text(new Rect(215,697,980,59),"Настройки общие для команды; меняет хост. Скрытие подсказок не ускоряет смену.\nИз обычного темпа нельзя вернуться в учебный для этого отеля.",small,muted);
        }
        void PaceConfirmation()
        {
            Text(new Rect(220,282,990,90),"Учебные ограничения будут сняты для этого отеля.",title,gold);
            Text(new Rect(220,408,990,162),"Включится обычный отсчёт смены. Гости смогут ждать, жаловаться и уезжать по обычным правилам.\n\nТекущие гости, вещи, деньги и освоенные действия останутся. Повторное обучение не создаётся; подсказки можно оставить включёнными.",normal);
            if(Button(new Rect(220,647,475,58),"Остаться в учебном темпе"))Game.Panel="briefing";
            if(Button(new Rect(720,647,485,58),"Включить обычный темп",Game.Session.IsHost&&HotelDirector.IsGuided(S),true)){Send("endGuidedOpening");Game.Panel="briefing";}
        }
        void Rooms()
        {
            var queue=S.guests.FirstOrDefault(g=>g.stage=="queue");
            Text(new Rect(202,334,485,52),queue!=null?"В очереди: "+queue.name+"  /  "+queue.trait:"Никто не ждёт заселения",bold,gold);
            int y=390;
            foreach(var room in S.rooms) {
                Box(new Rect(202,y,505,81),new Color(.13f,.20f,.20f));
                var guest=S.guests.Find(g=>g.id==room.guestId);
                Text(new Rect(216,y+9,320,28),"№ "+room.number+"  ·  "+(room.guestId!=0?guest?.name??"Занят":room.outOfService?"Закрыт":"Свободен"),bold);
                string reason=HotelSimulation.CheckInBlockReason(S,room.number);
                Text(new Rect(216,y+36,318,44),HotelPresentation.RoomProblems(room),small,room.bed==2&&!room.leak?muted:gold);
                if(Button(new Rect(543,y+7,150,38),reason==""?"Заселить":"Недоступно",reason==""))Send("checkin","",room.number);
                if(reason!="")Text(new Rect(543,y+45,152,36),HotelPresentation.ShortCheckInReason(reason),small,muted);
                y+=87;
            }
            Text(new Rect(746,334,445,37),"ВЫЕЗДЫ И ВПЕЧАТЛЕНИЯ",bold);
            y=384;
            foreach(var guest in S.guests.Where(g=>g.stage!="gone"&&g.stage!="leaving").Take(4)) {
                Text(new Rect(746,y,455,27),guest.name+"  ·  "+(guest.room==0?"ожидает":"№ "+guest.room),bold);
                Text(new Rect(746,y+29,460,34),GuestStatus(guest),small,guest.satisfaction<45?gold:muted);
                if(guest.stage=="checkout") {if(Button(new Rect(746,y+60,207,33),"Принять оплату"))Send("checkout","",guest.id);}
                else if(guest.room!=0 && !guest.compensated) {if(Button(new Rect(746,y+60,207,33),"Компенсация 25 ₽",S.cash>=25&&guest.stage=="staying"))Send("compensate","",guest.id);}
                if(Button(new Rect(965,y+60,230,33),"Запросы / причины")){selectedGuest=guest.id;guestScroll=Vector2.zero;Game.Panel="guest";}
                y+=96;
            }
        }
        void GuestDetails()
        {
            var guest=S.guests.Find(g=>g.id==selectedGuest);
            if(guest==null){Text(new Rect(210,280,980,100),"Гость уже покинул отель. Отзыв можно посмотреть в итогах смены.",normal);}
            else {
                Text(new Rect(210,270,990,45),guest.name+" · "+guest.trait+" · впечатление "+Mathf.RoundToInt(guest.satisfaction)+"/100",bold,gold);
                string text="ЧТО МОЖНО СДЕЛАТЬ СЕЙЧАС\n\n"+HotelPresentation.GuestIssues(S,guest)+"\n\nПОЧЕМУ ГОСТЬ ТАК ОЦЕНИВАЕТ ОТЕЛЬ\n\n"+(guest.memories.Count==0?"Пока нет значимых впечатлений.":string.Join("\n",guest.memories.Select(m=>"• "+m)))+"\n\nУже полученный отзыв не исчезает от ремонта. Новые хорошие впечатления могут улучшить итог.";
                float height=normal.CalcHeight(new GUIContent(text),950)+20;
                guestScroll=GUI.BeginScrollView(new Rect(210,325,1010,350),guestScroll,new Rect(0,0,975,height));
                Text(new Rect(5,0,950,height),text,normal);GUI.EndScrollView();
            }
            if(Button(new Rect(210,705,1000,44),"К номерам и гостям"))Game.Panel="reception";
        }
        string GuestStatus(GuestState g)
        {
            string mood=g.satisfaction>75?"Доволен":g.satisfaction>50?"Всё терпимо":g.satisfaction>25?"Раздражён":"Очень зол";
            if(g.stage=="queue")return "Ждёт у стойки · "+Mathf.RoundToInt(g.waited)+" сек.";
            if(g.stage=="checkout")return "Готов выехать · "+mood;
            return mood+" · "+(!g.luggageDelivered?"нужен чемодан":g.towelRequested?"просит полотенце":"отдыхает");
        }
        System.Collections.Generic.List<string> TaskLines()
        {
            var lines=HotelSimulation.BuildTasks(S);
            if(lines.Count==0)lines.Add("Всё под контролем. Проверьте гостей и подготовьте запас белья.");return lines;
        }
        void Tasks()
        {
            var lines=TaskLines();
            scroll=GUI.BeginScrollView(new Rect(202,335,660,412),scroll,new Rect(0,0,628,lines.Count*62));
            for(int i=0;i<lines.Count;i++){Box(new Rect(0,i*62,625,53),new Color(.13f,.20f,.20f));Text(new Rect(15,i*62+9,596,45),(i+1)+". "+lines[i],normal);}
            GUI.EndScrollView();
            Text(new Rect(892,340,322,40),"ШПАРГАЛКА СТАЖЁРА",bold,gold);
            Text(new Rect(892,396,322,314),"E — взять / применить\nQ — положить предмет\n\nРемонт: инструменты + раковина.\nУборка: швабра + лужа.\n\nГрязное бельё → корзина.\nМусор → бак.\n\nЧемодан — своему гостю.\nЗапасы пополняются между днями.",normal);
        }
        void Management()
        {
            Text(new Rect(202,336,490,38),"СМЕНА  "+S.day+"  /  "+Phase(S.phase),bold,gold);
            Text(new Rect(202,381,490,63),"Склад: бельё "+S.linenStock+" · полотенца "+S.towelStock+"\nПолучено: "+S.earned+" ₽   /   Расходы: "+S.expenses+" ₽",normal);
            if(S.phase=="preparation" && Button(new Rect(202,470,477,55),"Открыть отель",Game.Session.IsHost,true))Send("open");
            if((S.phase=="open"||S.phase=="closing") && Button(new Rect(202,470,477,55),"Закончить смену",Game.Session.IsHost,true))Game.Panel="finish-confirm";
            if(S.phase=="summary" && Button(new Rect(202,470,477,55),"К итогам дня"))Game.Panel="summary";
            if(Button(new Rect(202,543,232,47),"Сохранить",Game.Session.IsHost)){if(Game.Session.Save())Game.Notify("Отель сохранён.");}
            if(Button(new Rect(447,543,232,47),"Задачи"))Game.Panel="tasks";
            Text(new Rect(202,609,477,68),"Управление сменой — у стойки.\nПокупки и продажи номеров — у доски.",normal,muted);
            Text(new Rect(202,684,477,28),"Номера в продаже (переключает хост):",small,muted);
            for(int i=0;i<S.rooms.Count;i++) {
                var room=S.rooms[i];
                if(Button(new Rect(202+i*120,718,111,40),room.number+(room.outOfService?": закрыт":": открыт"),Game.Session.IsHost&&(S.phase=="preparation"||S.phase=="summary")))Send("toggleRoom","",room.number);
            }
            Text(new Rect(729,336,477,38),"ПЕРВЫЕ УЛУЧШЕНИЯ",bold,gold);
            Upgrade(729,397,"Второй набор инструментов","Чтобы коллега тоже мог чинить.","toolbox",S.secondToolbox);
            Upgrade(729,506,"Хорошие матрасы","Гостям будет немного уютнее.","beds",S.betterBeds);
            Upgrade(729,615,"Багажная тележка","Меньше таскать, больше успевать.","cart",S.cartUpgrade);
        }
        void FinishConfirmation()
        {
            int waiting=S.guests.Count(g=>g.stage=="queue");
            int staying=S.guests.Count(g=>g.stage=="walking"||g.stage=="staying"||g.stage=="checkout");
            Text(new Rect(220,282,990,70),"Завершить обслуживание и подвести итоги?",title,gold);
            Text(new Rect(220,380,990,210),"В очереди: "+waiting+". Они уйдут без заселения.\nПроживают / выезжают: "+staying+". Проживание будет рассчитано автоматически.\n\nНедоставленный багаж и полотенца повлияют на отзыв. Грязь и поломки останутся на следующий день. Это не пауза.",normal);
            if(Button(new Rect(220,635,480,57),"Продолжить обслуживание"))Game.Panel="management";
            if(Button(new Rect(725,635,480,57),"Завершить смену",Game.Session.IsHost,true)){Send("finish");if(S.phase!="summary")Game.Panel="management";}
        }
        void Upgrade(float x,float y,string titleText,string sub,string id,bool owned)
        {
            Text(new Rect(x,y,475,30),titleText,bold);Text(new Rect(x,y+33,475,30),sub,small,muted);
            int price=id=="toolbox"?HotelSimulation.ToolboxPrice:id=="beds"?HotelSimulation.BedsPrice:HotelSimulation.CartPrice;
            if(Button(new Rect(x,y+64,470,36),owned?"Уже куплено":"Купить за "+price+" ₽",!owned&&Game.Session.IsHost&&S.phase=="preparation"&&S.cash>=price))Send("upgrade",id);
        }
        void Summary()
        {
            Text(new Rect(204,285,520,66),S.served+" гостей обслужено",title,gold);
            Text(new Rect(204,369,470,140),"Выручка: "+S.earned+" ₽\nРасходы: "+S.expenses+" ₽\nНа счету: "+S.cash+" ₽",title);
            Text(new Rect(750,285,458,37),"ЧТО О НАС ГОВОРЯТ",bold,gold);
            int y=337;foreach(string review in S.reviews.AsEnumerable().Reverse().Take(4)){Text(new Rect(750,y,450,77),"«"+review+"»",normal);y+=88;}
            if(Button(new Rect(204,610,470,59),"Подготовиться к дню "+(S.day+1),Game.Session.IsHost,true)) {Send("nextday");Game.Session.Save();if(S.phase=="preparation")Game.OpenPanel("management");}
            Text(new Rect(204,687,1000,65),S.day==1?"Дальше: подготовка, покупка у доски и второй день с обычной нагрузкой.\nУборка и повреждения не исчезнут; улучшение останется в отеле.":"Отель сохраняется автоматически у хоста. Уборка и повреждения не исчезнут после перезапуска.",small,muted);
        }
        void Settings()
        {
            Frame("Настройки","Камера от первого лица. Изменения применяются сразу.");
            Text(new Rect(230,302,950,35),"Поле зрения: "+Mathf.RoundToInt(Game.Fov)+"°",bold);
            Game.Fov=GUI.HorizontalSlider(new Rect(230,352,950,22),Game.Fov,65,105);
            Text(new Rect(230,401,950,35),"Чувствительность мыши: "+Game.Sensitivity.ToString("0.00"),bold);
            Game.Sensitivity=GUI.HorizontalSlider(new Rect(230,451,950,22),Game.Sensitivity,.025f,.25f);
            Text(new Rect(230,500,950,35),"Громкость: "+Mathf.RoundToInt(Game.Volume*100)+"%",bold);
            Game.Volume=GUI.HorizontalSlider(new Rect(230,550,950,22),Game.Volume,0,1);
            if(Button(new Rect(230,588,450,48),"Инверсия Y: "+(Game.InvertY?"вкл":"выкл")))Game.InvertY=!Game.InvertY;
            if(Button(new Rect(704,588,476,48),"Покачивание камеры: "+(Game.Bob?"вкл":"выкл")))Game.Bob=!Game.Bob;
            if(Button(new Rect(230,650,450,48),"Движения рук: "+(Game.HandMotion?"вкл":"выкл")))Game.HandMotion=!Game.HandMotion;
            if(Button(new Rect(704,650,476,48),"Звуки протечек: "+(Game.AmbientSound?"вкл":"выкл")))Game.AmbientSound=!Game.AmbientSound;
            if(Button(new Rect(230,718,950,49),"Сохранить настройки",true,true)){Game.StoreSettings();Game.OpenPanel(Game.Playing?"pause":"menu");}
        }
        void SteamMenu()
        {
            Frame("Кооператив через Steam — тест", "Экспериментальная интеграция. AppID 480 (Spacewar), а не собственный Steam-продукт.");
            Text(new Rect(216,287,1000,96),"У обоих игроков должен быть запущен Steam с разными аккаунтами. Лобби доступно друзьям Steam. Кнопки ниже явно включают тестовый AppID 480; обычный запуск игры Steam не инициализирует.",normal,gold);
            bool saved=System.IO.File.Exists(Game.Session.SavePath)||System.IO.File.Exists(Game.Session.SavePath+".bak");
            if(Button(new Rect(216,414,468,57),"Продолжить отель через Steam",saved,true)) {
                Game.Session.Disconnect(false);Game.Session.HostSteam(true);Game.OpenPanel("");
            }
            if(Button(new Rect(216,491,468,57),"Создать новый Steam-отель")) {
                Game.Session.Disconnect(false);Game.Session.HostSteam(false);Game.OpenPanel("");
            }
            Text(new Rect(216,575,468,78),"При создании новой игры прежний отель сохраняется в архиве. ID лобби можно скопировать в меню ESC и передать другу.",small,muted);
            Text(new Rect(738,414,470,33),"ID ЛОББИ ДРУГА",bold);
            steamLobby=GUI.TextField(new Rect(738,462,466,49),steamLobby,24);
            if(Button(new Rect(738,532,466,57),"Присоединиться через Steam",true,true)) {
                if(!ulong.TryParse(steamLobby,out ulong id)||id==0)Game.Notify("Введите числовой ID лобби.");
                else{Game.Session.Disconnect(false);Game.Session.JoinSteam(id);Game.OpenPanel("");}
            }
            Text(new Rect(738,619,470,76),"Тест через интернет требует двух компьютеров. Прямое подключение по IP доступно в основном меню.",small,muted);
            if(Button(new Rect(216,701,478,45),"Сбросить Steam-подключение"))Game.Session.ResetSteam();
            if(Button(new Rect(714,701,490,45),"Назад"))Game.OpenPanel("menu");
        }
        void Recovery()
        {
            Frame("Сетевая сессия остановлена","Сохранение остаётся обязанностью хоста даже после отключения сети.");
            Text(new Rect(220,288,990,100),Game.Session.Status,normal,gold);
            Text(new Rect(220,407,990,150),string.IsNullOrEmpty(Game.Session.SaveError)?"Прогресс записан: "+Game.Session.LastSaved+". Вернитесь в меню и выберите «Продолжить отель».":"Ошибка записи: "+Game.Session.SaveError+"\n\nЖивое состояние сохранено в памяти. Освободите место / закройте программу, блокирующую файл, и повторите. Не завершайте процесс принудительно.",normal);
            if(Button(new Rect(220,642,990,60),"Сохранить и вернуться в меню",true,true))Game.Leave();
        }
        void Pause()
        {
            Frame("Перевести дух","Меню не ставит мир на паузу. Учебный таймер ждёт освоения основ; работа и движение продолжаются.");
            if(Button(new Rect(320,305,800,58),"Вернуться в отель",true,true))Game.OpenPanel("");
            if(Button(new Rect(320,379,800,52),"Настройки камеры и звука"))Game.OpenPanel("settings");
            if(Button(new Rect(320,449,800,52),"Задачи и состояние номеров"))Game.OpenPanel("tasks");
            if(Button(new Rect(320,519,800,52),"Сохранить отель",Game.Session.IsHost)){if(Game.Session.Save())Game.Notify("Сохранено.");}
            if(Button(new Rect(320,586,800,48),"План смены / подсказки / учебный темп"))Game.Panel="briefing";
            if(Button(new Rect(320,650,800,45),Game.Session.IsHost?"Сохранить и закрыть сессию":"Покинуть сессию"))Game.Leave();
            Text(new Rect(320,710,545,45),Game.Session.Status+"\nСохранение хранится на компьютере хоста.",small,muted);
            if(Game.Session.SteamMode && Button(new Rect(875,710,240,40),"Скопировать ID лобби")){GUIUtility.systemCopyBuffer=HotelSteam.LobbyId.ToString();Game.Notify("ID лобби скопирован.");}
        }
        void Send(string action,string target="",int number=0){Game.Session.Send(new HotelCommand(action,target,number));}
        static string Phase(string phase){switch(phase){case "preparation":return "ПОДГОТОВКА";case "open":return "ОТЕЛЬ ОТКРЫТ";case "closing":return "ЗАКРЫТИЕ";case "summary":return "ИТОГИ";default:return phase;}}
    }
}
