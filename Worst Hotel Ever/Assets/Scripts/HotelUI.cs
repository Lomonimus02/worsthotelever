using System;
using System.Linq;
using UnityEngine;

namespace WorstHotel
{
    // Resolution-independent UI, built in code so a clean checkout needs no manual scene wiring.
    public sealed partial class HotelUI : MonoBehaviour
    {
        public HotelGame Game;
        HotelState S=>Game.Session.State;
        GUIStyle small,normal,bold,title,huge,button;
        readonly Color ink=new Color(.095f,.135f,.14f), paper=new Color(.96f,.93f,.85f), muted=new Color(.82f,.86f,.81f), red=new Color(.68f,.24f,.22f), gold=new Color(.9f,.68f,.36f), green=new Color(.35f,.71f,.53f);
        string address="127.0.0.1",port="7777",password="",steamLobby="";
        bool hostLoad;
        bool paperSurface;
        public string JournalTutorialBodyForTest { get; private set; } = "";
        Vector2 scroll, guestScroll; string lastPhase="";
        int selectedGuest;
        int selectedRoom=101, refusalGuest;
        string refusalReservation="", mvpWorld="", mvpScrollPanel="";
        Vector2 mvpScroll;
        bool Danger => HotelDangerRules.Enabled(S);
        readonly System.Collections.Generic.Dictionary<string,Vector2> mvpScrolls=new System.Collections.Generic.Dictionary<string,Vector2>();
        void Init()
        {
            Font font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            small=new GUIStyle(GUI.skin.label){font=font,fontSize=17,wordWrap=true,richText=false};small.normal.textColor=paper;
            normal=new GUIStyle(small){fontSize=19};
            bold=new GUIStyle(normal){fontStyle=FontStyle.Bold};
            title=new GUIStyle(bold){fontSize=32};
            huge=new GUIStyle(bold){fontSize=70};
            button=new GUIStyle(bold){alignment=TextAnchor.MiddleCenter,wordWrap=true,fontSize=18};button.normal.textColor=paper;
            GUI.skin.textField.font=font;GUI.skin.textField.fontSize=22;
            GUI.skin.textField.padding=new RectOffset(12,12,10,10);
            GUI.skin.horizontalSlider.fixedHeight=18; GUI.skin.horizontalSliderThumb.fixedWidth=20;GUI.skin.horizontalSliderThumb.fixedHeight=22;
            InitTheme();
        }
        void OnGUI()
        {
            if(Game==null||Game.View==null)return;if(normal==null)Init();
            paperSurface=false;
            if(Event.current.type==EventType.Repaint)JournalTutorialBodyForTest="";
            float scale=Mathf.Min(Screen.width/1440f,Screen.height/900f);
            GUI.matrix=Matrix4x4.TRS(new Vector3((Screen.width-1440*scale)*.5f,(Screen.height-900*scale)*.5f,0),Quaternion.identity,Vector3.one*scale);
            if(Game.Playing) {
                if(S.mvp!=null&&mvpWorld!=S.worldId){mvpWorld=S.worldId;lastPhase="";selectedGuest=0;selectedRoom=101;refusalGuest=0;refusalReservation="";mvpScrolls.Clear();folds.Clear();mvpScroll=Vector2.zero;mvpScrollPanel="";}
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
                paperSurface=false;
                Box(new Rect(430,326,580,245),ink);Text(new Rect(459,352,520,60),"Подключаемся к отелю…",title);
                Text(new Rect(459,424,520,50),Game.Session.Status,normal);
                if(Button(new Rect(459,498,520,47),"Отменить")){Game.Session.Disconnect(false);Game.OpenPanel("menu");}
            }
            paperSurface=false;
            DrawToast();
            // Only an actual threat survives above menus, not an entire second dashboard.
            if(Game.Playing)DrawAlert();
            GUI.matrix=Matrix4x4.identity;
        }
        void Box(Rect rect,Color color) {Color old=GUI.color;GUI.color=color;GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=old;}
        void Text(Rect rect,string text,GUIStyle style=null,Color? color=null) {
            var s=style??normal;Color old=s.normal.textColor;s.normal.textColor=ThemeText(color??paper);
            GUI.Label(rect,text,s);s.normal.textColor=old;
        }
        bool Button(Rect r,string label,bool enabled=true,bool accent=false)
        {
            bool hover=r.Contains(Event.current.mousePosition);
            DrawButtonSurface(r,enabled,accent,hover);
            bool old=GUI.enabled;GUI.enabled=enabled;
            Color oldText=button.normal.textColor;
            button.normal.textColor=paperSurface&&(!accent||!enabled)?documentInk:paper;
            button.hover.textColor=button.normal.textColor;button.active.textColor=button.normal.textColor;button.focused.textColor=button.normal.textColor;
            bool pressed=GUI.Button(r,label,button);button.normal.textColor=oldText;GUI.enabled=old;return pressed;
        }
        void Menu()
        {
            DrawMenuPaper();
            Box(new Rect(49,67,62,5),stamp);Text(new Rect(49,90,450,30),"КОМАНДА НУЖНА. ОПЫТ НЕОБЯЗАТЕЛЕН.",small,gold);
            Text(new Rect(43,140,510,260),"WORST\nHOTEL\nEVER",huge);
            Text(new Rect(49,388,465,78),"4 стартовых номера + 2 покупаемых.\nДва сотрудника. Опасные контракты.",normal);
            Text(new Rect(49,837,465,53),"1.1.1-ui-alpha1 · 1–2 ИГРОКА\nТЕСТОВАЯ СБОРКА · НЕ РЕЛИЗ",small,muted);
            Box(new Rect(998,24,397,74),documentPaper);
            Text(new Rect(1017,36,366,50),"ОТЕЛЬ «ПОЧТИ ГРАНД»\n★  НАЧНИТЕ С ЧИСТОГО ПОЛОТЕНЦА",small,paper);
            if(Game.Session.PlaytestSlot){Box(new Rect(800,112,595,60),documentPaper);Text(new Rect(816,121,560,48),"Тестовый отель · основной сейв не затронут",small,gold);}
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
            if(!string.IsNullOrEmpty(Game.Session.Status)) {Box(new Rect(595,745,790,100),documentPaper);Text(new Rect(615,760,750,72),Game.Session.Status,normal);}
        }
        void Frame(string heading,string sub)
        {
            DrawDocument(new Rect(170,136,1100,645));
            Text(new Rect(202,162,970,45),heading,title);
            Text(new Rect(204,212,970,35),sub,small,muted);
            if(Button(new Rect(1190,158,49,44),"×"))Game.OpenPanel(Game.Playing?"":"menu");
        }
        void Window()
        {
            if(S.mvp!=null){MvpWindow();return;}
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
                Box(new Rect(202,y,505,81),documentShade);
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
            var lines=S.mvp==null?HotelSimulation.BuildTasks(S):HotelPresentation.MvpTasks(S);
            if(lines.Count==0)lines.Add("Всё под контролем. Проверьте гостей и подготовьте запас белья.");return lines;
        }
        void Tasks()
        {
            var lines=TaskLines();
            scroll=GUI.BeginScrollView(new Rect(202,335,660,412),scroll,new Rect(0,0,628,lines.Count*62));
            for(int i=0;i<lines.Count;i++){Box(new Rect(0,i*62,625,53),documentShade);Text(new Rect(15,i*62+9,596,45),(i+1)+". "+lines[i],normal);}
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
        // MVP uses the same paper/ink palette and virtual canvas as the original UI.
        // Every list lives in this bounded viewport; text and cards grow with their contents.
        void MvpWindow()
        {
            string panel=Game.Panel;
            Frame(panel=="summary"?"Итоги дня "+S.day:panel=="finish-confirm"?"Завершение смены":panel=="refuse-confirm"?"Отказ в размещении":"Журнал дежурного",
                "ОТЕЛЬ «ПОЧТИ ГРАНД»     ·     Мир не на паузе. TAB / ESC — вернуться в отель.");
            if(panel=="pace-confirm"){PaceConfirmation();return;}
            string[] labels={"Дела","Гости","Номера","Смена","Заезды","Помощь"};
            string[] panels={"tasks","guests","reception","management","schedule","briefing"};
            for(int i=0;i<labels.Length;i++)
                if(Button(new Rect(202+i*174,258,164,42),labels[i],true,panel==panels[i]||(i==2&&panel=="rooms")||(i==1&&panel=="guest")||(i==0&&panel=="operations")))Game.OpenPanel(panels[i]);
            if(mvpScrollPanel!=panel){if(mvpScrollPanel!="")mvpScrolls[mvpScrollPanel]=mvpScroll;mvpScroll=mvpScrolls.TryGetValue(panel,out var saved)?saved:Vector2.zero;mvpScrollPanel=panel;}
            GUILayout.BeginArea(new Rect(202,319,1036,382));
            mvpScroll=GUILayout.BeginScrollView(mvpScroll,false,false,GUILayout.Width(1036),GUILayout.Height(382));
            GUILayout.BeginVertical(GUILayout.Width(1002));
            if(panel=="guests")MvpGuests();
            else if(panel=="guest")MvpGuestDetails();
            else if(panel=="tasks"||panel=="operations")MvpOperations();
            else if(panel=="schedule")MvpSchedule();
            else if(panel=="management")MvpManagement();
            else if(panel=="summary")MvpSummary();
            else if(panel=="briefing")MvpBriefing();
            else if(panel=="finish-confirm")MvpFinishConfirmation();
            else if(panel=="abandon-confirm")DangerAbandonConfirmation();
            else if(panel=="refuse-confirm")MvpRefuseConfirmation();
            else MvpRooms();
            GUILayout.Space(12);GUILayout.EndVertical();GUILayout.EndScrollView();GUILayout.EndArea();
            Box(new Rect(202,709,1036,2),new Color(.24f,.31f,.29f));
            Text(new Rect(206,718,770,48),"День "+S.day+" · "+Phase(S.phase)+" · "+S.cash+" ₽ · ★ "+S.mvp.reputation.ToString("0.0")+" / 5\n"+
                (S.phase=="preparation"?"Смена — у стойки. Контракт и покупки — у доски.":"Заселение и оплата — у стойки."),small,muted);
            if(Button(new Rect(1018,721,220,43),Danger&&HotelPresentation.DangerTerminal(S)&&panel!="summary"?"Результат смены":"Закрыть журнал"))
                Game.OpenPanel(Danger&&HotelPresentation.DangerTerminal(S)&&panel!="summary"?"summary":"");
        }
        void MvpText(string value,GUIStyle style=null,Color? color=null)
        {
            GUIStyle use=style??normal;Color old=use.normal.textColor;
            use.normal.textColor=ThemeText(color??paper);
            GUILayout.Label(value??"",use,GUILayout.ExpandWidth(true));use.normal.textColor=old;
        }
        void MvpSection(string heading)
        {
            GUILayout.Space(15);MvpText(heading,bold,gold);GUILayout.Space(6);
        }
        bool MvpButton(string label,bool enabled=true,bool accent=false,float width=0)
        {
            float h=Mathf.Max(40,button.CalcHeight(new GUIContent(label),width>0?width:970)+14);
            Rect rect=width>0?GUILayoutUtility.GetRect(width,h,GUILayout.Width(width),GUILayout.Height(h)):
                GUILayoutUtility.GetRect(new GUIContent(label),button,GUILayout.Height(h),GUILayout.ExpandWidth(true));
            return Button(rect,label,enabled,accent);
        }
        void MvpReason(string reason){if(!string.IsNullOrEmpty(reason))MvpText(reason,small,muted);}
        string MvpStation(string station,bool hostOnly=false,bool prepOnly=false)
        {
            return HotelPresentation.StationReason(S,Game.Session.LocalId,Game.Session.IsHost,station,hostOnly,prepOnly);
        }
        void MvpGuestSend(string action,int guest,int room=0){Game.Session.Send(HotelPresentation.GuestCommand(action,guest,room));}
        void MvpSelectGuest(GuestState guest,string panel)
        {
            selectedGuest=guest.id;Game.Panel=panel;mvpScrolls[panel]=Vector2.zero;
        }
        void MvpRooms()
        {
            GuestState chosen=S.guests.Find(g=>g.id==selectedGuest);
            MvpSection("НОМЕРА · "+S.rooms.Count(r=>r.mvp?.owned==true)+" / 6 приобретено");
            MvpText(chosen==null?"Гость не выбран. Сначала выберите человека во вкладке «Гости».":"Выбран: "+chosen.name+" · "+HotelPresentation.StatusName(chosen.stage),bold);
            if(chosen!=null){MvpText(HotelPresentation.GuestContract(chosen.mvp),small);MvpText(HotelPresentation.Requirements(chosen.mvp),small,muted);}
            if(MvpButton("Выбрать другого гостя"))Game.Panel="guests";
            string desk=MvpStation("desk");MvpReason(desk);
            foreach(var room in S.rooms.OrderBy(r=>r.number))
            {
                var occupant=S.guests.Find(g=>g.id==room.guestId);
                MvpSection("№ "+room.number+" · "+(room.mvp?.owned!=true?"Не приобретён":occupant!=null?occupant.name:room.outOfService?"Закрыт для продаж":"Свободен"));
                MvpText(HotelPresentation.RoomProblems(room),normal,room.mvp?.owned==true?paper:muted);
                if(room.mvp?.owned!=true){if(MvpButton("Показать покупку № "+room.number))Game.Panel="management";continue;}
                MvpText("Мест: "+room.mvp.capacity+" · кровать "+room.mvp.bedQuality+" · ТВ "+room.mvp.tvQuality,small,muted);
                if(Fold("room-"+room.number,"Оборудование и отделка № "+room.number)) {
                    MvpText("Отделка: "+HotelPresentation.FinishName(room.mvp.finishId)+" · шум "+HotelPresentation.Percent(room.mvp.noise),small,muted);
                    foreach(string kind in new[]{"sink","toilet","tv","lamp"})MvpText(HotelPresentation.EquipmentStatus(S,room.number,kind),small);
                }
                string reason=HotelPresentation.AssignmentReason(S,selectedGuest,room.number);
                bool relocate=chosen!=null&&chosen.stage!="queue";
                if(MvpButton((relocate?"Переселить выбранного гостя":"Заселить выбранного гостя")+" → № "+room.number,reason==""&&desk=="",true))MvpGuestSend(relocate?"relocate":"checkin",selectedGuest,room.number);
                MvpReason(reason);
                if(occupant!=null&&MvpButton("Запросы и впечатления: "+occupant.name))MvpSelectGuest(occupant,"guest");
            }
        }
        void MvpGuests()
        {
            var guests=S.guests.OrderBy(g=>g.stage=="queue"?0:g.stage=="checkout"?1:g.stage=="walking"||g.stage=="staying"?2:3).ThenBy(g=>g.id).ToList();
            MvpSection("ГОСТИ · в очереди "+guests.Count(g=>g.stage=="queue")+" · проживают "+guests.Count(g=>g.stage=="walking"||g.stage=="staying"));
            MvpText("Выберите гостя, затем подходящий номер.",small,muted);
            if(guests.Count==0)MvpText("Гости ещё не прибыли. Откройте отель у стойки; план прибытия есть в расписании.");
            foreach(var guest in guests)
            {
                MvpSection((guest.id==selectedGuest?"ВЫБРАН · ":"")+guest.name+" · "+HotelPresentation.StatusName(guest.stage)+(guest.room>0?" · № "+guest.room:""));
                MvpText(HotelPresentation.GuestContract(guest.mvp));
                MvpText("Впечатление: "+Mathf.RoundToInt(guest.satisfaction)+" / 100"+(guest.stage=="queue"?" · ожидание "+Mathf.RoundToInt(guest.waited)+" с":""),small,guest.satisfaction<45?gold:muted);
                if(guest.stage=="queue"||guest.stage=="walking"||guest.stage=="staying")
                    if(MvpButton(guest.stage=="queue"?"Выбрать и подобрать номер":"Выбрать для переселения",true,true))MvpSelectGuest(guest,"reception");
                if(MvpButton("Условия, запросы и причины оценки"))MvpSelectGuest(guest,"guest");
                if(guest.stage=="checkout")MvpCheckout(guest);
            }
        }
        void MvpCheckout(GuestState guest)
        {
            string reason=MvpStation("desk");
            if(MvpButton(guest.paid?"Проживание оплачено":"Принять оплату: "+guest.name,reason==""&&!guest.paid,true))MvpGuestSend("checkout",guest.id);
            MvpReason(reason);
        }
        void MvpGuestDetails()
        {
            var guest=S.guests.Find(g=>g.id==selectedGuest);
            if(guest==null){MvpText("Выбранный гость уже выбыл из журнала. Его отзыв остаётся в итогах.");if(MvpButton("К списку гостей"))Game.Panel="guests";return;}
            MvpSection(guest.name+" · "+HotelPresentation.StatusName(guest.stage)+" · "+Mathf.RoundToInt(guest.satisfaction)+" / 100");
            MvpText(HotelPresentation.GuestContract(guest.mvp));MvpText(HotelPresentation.Requirements(guest.mvp));
            if(!string.IsNullOrEmpty(guest.mvp?.groupId))MvpText("Группа: "+guest.mvp.groupId+" · партия: "+guest.mvp.partyId,small,muted);
            string issues=HotelPresentation.GuestIssues(S,guest);
            MvpSection("ЗАПРОСЫ И ПРИЧИНЫ");MvpText(string.IsNullOrEmpty(issues)?"Активных жалоб нет. Запросы обслуживания показаны ниже.":issues);
            foreach(var request in S.mvp.requests.Where(r=>r.guestId==guest.id))
                MvpText(HotelPresentation.RequestName(request.kind)+" · "+HotelPresentation.RequestDeadline(S,request)+" · "+MvpTargetName(request.target),small,request.status=="open"?gold:muted);
            foreach(var complaint in S.mvp.complaints.Where(c=>c.guestId==guest.id&&c.status=="active"))
                MvpText("Жалоба: "+MvpComplaintName(complaint.category)+" · эскалация "+complaint.escalation+" / 2",small,gold);
            MvpSection("ВПЕЧАТЛЕНИЯ");
            if(guest.memories.Count==0)MvpText("Пока нет значимых впечатлений.",small,muted);
            foreach(string memory in guest.memories)MvpText("• "+memory,small);
            string desk=MvpStation("desk");
            if(guest.stage=="queue"||guest.stage=="walking"||guest.stage=="staying")
                if(MvpButton(guest.stage=="queue"?"Подобрать номер":"Подобрать номер для переселения",true,true))Game.Panel="reception";
            if(guest.stage=="checkout")MvpCheckout(guest);
            if(guest.stage=="staying"||guest.stage=="checkout")
            {
                if(MvpButton(guest.compensated?"Компенсация уже выдана":"Компенсация · 25 ₽",desk==""&&!guest.compensated&&S.cash>=25))MvpGuestSend("compensate",guest.id);
                if(S.cash<25&&!guest.compensated)MvpReason("Для компенсации нужно 25 ₽.");
                MvpText("Компенсация улучшает впечатление один раз. Ремонт, доставка и уборка выполняются отдельно.",small,muted);
            }
            if(guest.stage=="queue"&&MvpButton("Отказать этому гостю…",desk=="")){refusalGuest=guest.id;refusalReservation="";Game.Panel="refuse-confirm";}
            MvpReason(desk);
        }
        void MvpOperations()
        {
            if(Danger&&Game.Session.IsHost&&HotelDangerRules.HostCanForfeit(S)) {
                MvpSection("НЕКОМУ ПРОДОЛЖАТЬ СМЕНУ");
                MvpText("Можно ждать возвращения коллеги или признать провал. Покупки останутся.");
                if(MvpButton("Признать провал…",true,true))Game.OpenPanel("abandon-confirm");
            }
            var hint=HotelOnboarding.GetHint(S,Game.Session.LocalId);
            if(hint!=null&&HotelDangerRules.CanAct(S,Game.Session.LocalId)) {
                MvpSection("СЛЕДУЮЩИЙ ШАГ · "+hint.title);
                if(Fold("current-lesson","Как это сделать"))JournalHintBody(hint.body);
            }
            if(Danger) {
                MvpSection("СМЕНА · "+HotelDangerRules.ModeName(S.danger.mode));
                MvpText(HotelDangerRules.Objective(S));
                if(HotelPresentation.DangerTerminal(S)&&MvpButton("Открыть итог смены",true,true))Game.OpenPanel("summary");
            }
            MvpSection("ЧТО НУЖНО СДЕЛАТЬ");foreach(string line in TaskLines())MvpText("• "+line);
            MvpSection("ЗАПРОСЫ ГОСТЕЙ");
            foreach(var request in S.mvp.requests.Where(r=>r.status=="open").OrderBy(r=>r.dueAt))
            {
                var guest=S.guests.Find(g=>g.id==request.guestId);
                MvpText(HotelPresentation.RequestName(request.kind)+" · "+(guest?.name??"Гость #"+request.guestId)+" · "+HotelPresentation.RequestDeadline(S,request));
                if(guest!=null&&MvpButton("Открыть гостя",true,false,240))MvpSelectGuest(guest,"guest");
            }
            if(!S.mvp.requests.Any(r=>r.status=="open"))MvpText("Открытых запросов нет.",small,muted);
            if(Danger&&Fold("danger-details","Команда, аптечки и аварии"))DangerOperations();
            if(Fold("utilities","Вода, электричество и нагрузка")) {
                MvpText(HotelMvpDirector.Status(S));
                MvpText("Вода: "+(S.mvp.utilities.waterFault?"АВАРИЯ":"работает")+" · износ "+HotelPresentation.Percent(S.mvp.utilities.waterWear));
                MvpText("Электричество: "+(S.mvp.utilities.powerFault?"АВАРИЯ":"работает")+" · износ "+HotelPresentation.Percent(S.mvp.utilities.powerWear));
                MvpText("Оба щита — у склада в лобби. Для ремонта нужны инструменты и удержание E.",small,muted);
            }
            if(Fold("supplies","Запасы и памятка по работе")) {
            MvpText("Бельё: "+S.linenStock+" · чистые полотенца: "+S.towelStock+" · кофе: "+S.mvp.coffeeStock);
            MvpText("Полотенце → стойка в номере. Кофе: удержать E в лобби → E у столика гостя. Уборка: швабра → грязь или лужа. Багаж: проверьте владельца и перенесите к его подставке. Большие чемоданы можно перевозить тележкой.",normal);
            MvpText("Грязное бельё и полотенца → приёмник. Мусор → бак. Туалет чинят вантузом; раковину, ТВ, лампу и общие системы — инструментами. Неубранная вода остаётся после ремонта.",normal);
            }
            MvpSection(Danger?"ТЕКУЩАЯ РАБОТА ПОДКЛЮЧЁННЫХ":"КОМАНДА");
            foreach(var player in S.players)MvpText((player.id==Game.Session.LocalId?"Вы": "Коллега")+" · "+(player.workTarget!=""?MvpTargetName(player.workTarget)+" · "+HotelPresentation.Percent(player.workProgress):HotelPresentation.HeldLabel(S,S.items.Find(i=>i.id==player.held&&!i.consumed))),small);
        }
        void MvpSchedule()
        {
            MvpSection("ПРОГНОЗ · ДЕНЬ "+S.day);
            var forecast=S.mvp.schedule.Where(e=>e.day>=S.day).OrderBy(e=>e.day).ThenBy(e=>e.time).ToList();
            MvpText("Запланировано прибытий: "+forecast.Count(e=>e.status=="planned")+" · цена: "+HotelPresentation.PriceName(S.mvp.priceMode)+" · репутация: "+S.mvp.reputation.ToString("0.0")+" / 5");
            MvpText("Прогноз сохранён вместе с отелем. Уже согласованные тарифы гостей и бронирований не меняются при смене цен. Ночь с заездом в день 1 и выездом до дня 2 оплачивается один раз; расчёт — в последней четверти дня 1.",small,muted);
            if(forecast.Count==0)MvpText("Запланированных прибытий пока нет.",small,muted);
            foreach(var entry in forecast)
            {
                MvpSection("День "+entry.day+" · "+MvpTime(entry.time)+" от открытия · "+HotelPresentation.SourceName(entry.source));
                MvpText(HotelPresentation.StatusName(entry.status)+" · "+HotelPresentation.GuestContract(entry.contract),small);
                if(!string.IsNullOrEmpty(entry.groupId))MvpText("Группа: "+entry.groupId,small,muted);
            }
            MvpSection("БРОНИРОВАНИЯ");
            string desk=MvpStation("desk");MvpReason(desk);
            foreach(var booking in S.mvp.reservations.OrderBy(r=>r.arrivalDay).ThenBy(r=>r.room))
            {
                MvpText("№ "+booking.room+" · дни ["+booking.arrivalDay+", "+booking.departureDay+") · "+HotelPresentation.StatusName(booking.status),bold);
                MvpText(HotelPresentation.SourceName(booking.source)+" · "+HotelPresentation.GuestContract(booking.contract),small);
                MvpText("Бронь "+booking.id+(string.IsNullOrEmpty(booking.groupId)?"":" · группа "+booking.groupId),small,muted);
                if(booking.status=="confirmed"&&MvpButton("Отказаться от этой брони…",desk=="")){refusalReservation=booking.id;refusalGuest=0;Game.Panel="refuse-confirm";}
                GUILayout.Space(10);
            }
            if(S.mvp.reservations.Count==0)MvpText("Бронирований пока нет.",small,muted);MvpReason(desk);
            MvpSection("ГРУППЫ");
            foreach(var group in S.mvp.groups)
                MvpText(HotelPresentation.SourceName(group.source)+" · день "+group.arrivalDay+" · "+HotelPresentation.StatusName(group.status)+"\nЗаселено: "+group.admitted+" · отказов: "+group.refused+" · завершено: "+group.completed+" · броней: "+group.reservationIds.Count+"\n"+group.id,normal);
            if(S.mvp.groups.Count==0)MvpText("Групповых заездов пока нет.",small,muted);
        }
        void MvpManagement()
        {
            string desk=MvpStation("desk",true),board=MvpStation("board",true,true);
            if(S.phase=="preparation"&&MvpButton("Открыть отель",desk=="",true))Send("open");
            if((S.phase=="open"||S.phase=="closing")&&MvpButton("Завершить смену…",desk=="",true))Game.OpenPanel("finish-confirm");
            if(S.phase=="summary"&&MvpButton("Открыть итоги дня",true,true))Game.OpenPanel("summary");
            MvpReason(desk);
            if(Danger)DangerContract();
            MvpSection("ПОДГОТОВКА И БЮДЖЕТ");
            MvpText("Баланс: "+S.cash+" ₽ · выручка: "+S.earned+" ₽ · расходы: "+S.expenses+" ₽");
            if(S.cash<0)MvpText("Долг: "+(-S.cash)+" ₽. Продолжайте обслуживание и принимайте оплату. Необязательные улучшения не покупаются в долг; базовое пополнение проходит при подготовке.",normal,gold);
            MvpText("Запасы: бельё "+S.linenStock+", полотенца "+S.towelStock+", кофе "+S.mvp.coffeeStock+". Оставшиеся поломки, грязь и продолжающие проживание гости сохраняются.",small,muted);
            if(MvpButton("Расписание и сохранённый прогноз"))Game.Panel="schedule";
            MvpSection("ЦЕНЫ · "+HotelPresentation.PriceName(S.mvp.priceMode));
            MvpText("Цена влияет на новые предложения. Действующие договорённости сохраняют согласованный тариф.",small,muted);
            GUILayout.BeginHorizontal();
            foreach(string price in new[]{"low","normal","high"})if(MvpButton(HotelPresentation.PriceName(price)+(S.mvp.priceMode==price?" · выбрано":""),board==""&&S.mvp.priceMode!=price,false,320))Send("price",price);
            GUILayout.EndHorizontal();MvpReason(board);
            MvpSection("НОМЕР ДЛЯ МЕБЕЛИ И ОТДЕЛКИ · № "+selectedRoom);
            GUILayout.BeginHorizontal();
            foreach(var room in S.rooms.OrderBy(r=>r.number))if(MvpButton("№ "+room.number,room.mvp?.owned==true,selectedRoom==room.number,156))selectedRoom=room.number;
            GUILayout.EndHorizontal();
            MvpSection("УЛУЧШЕНИЯ");
            foreach(var upgrade in HotelUpgradeCatalog.All)
            {
                int room=upgrade.id=="bed"||upgrade.id=="tv"?selectedRoom:0;
                string reason=HotelOperationsRules.PurchaseBlockReason(S,upgrade.id,room);
                MvpText(upgrade.name+(room>0?" · № "+room:""),bold);MvpText(upgrade.description,small,muted);
                if(MvpButton("Купить · "+upgrade.price+" ₽",board==""&&reason==""))Send("upgrade",upgrade.id,room);
                MvpReason(reason);GUILayout.Space(10);
            }
            var selected=S.rooms.Find(r=>r.number==selectedRoom);
            MvpSection("ОТДЕЛКА № "+selectedRoom+" · "+HotelPresentation.FinishName(selected?.mvp?.finishId));
            MvpText("Отделка меняет вид комнаты. Вместимость, качество мебели и чистота учитываются отдельно.",small,muted);
            foreach(string finish in new[]{"original","warm","cool"})
            {
                string reason=HotelOperationsRules.FinishBlockReason(S,finish,selectedRoom);
                if(MvpButton(HotelPresentation.FinishName(finish)+" · "+HotelUpgradeCatalog.FinishPrice(finish)+" ₽"+(selected?.mvp?.finishId==finish?" · выбрано":""),board==""&&reason==""))Send("finishRoom",finish,selectedRoom);
                MvpReason(reason);
            }
            if(selected?.guestId!=0)MvpReason("Отделка доступна после выезда гостя.");
            MvpSection("ПРОДАЖА НОМЕРОВ");
            foreach(var room in S.rooms.Where(r=>r.mvp?.owned==true).OrderBy(r=>r.number))
                if(MvpButton("№ "+room.number+" · "+(room.outOfService?"Открыть для продаж":"Закрыть для продаж"),board==""&&room.guestId==0))Send("toggleRoom","",room.number);
            MvpText("Занятый номер нельзя закрыть. Закрытие ограничивает доступные места; существующие брони проверяет отель.",small,muted);
            MvpReviewsAndLedger();
        }
        void DangerContract()
        {
            var danger=S.danger;
            MvpSection("КОНТРАКТ · "+HotelDangerRules.ModeName(danger.mode));
            MvpText(HotelDangerRules.Objective(S),bold);
            MvpText("Премия: "+danger.reward+" ₽ · расход при провале: "+danger.penalty+" ₽ · минимум: "+HotelPresentation.Seconds(danger.minimumSeconds),normal,gold);
            if(S.phase=="preparation") {
                MvpText("Выберите риск до открытия отеля.",small,muted);
                string board=MvpStation("board",true,true);
                GUILayout.BeginHorizontal();
                foreach(string mode in new[]{"relief","standard","bold"}) {
                    if(MvpButton(HotelDangerRules.ModeName(mode)+(danger.mode==mode?" · выбран":""),board==""&&danger.mode!=mode,danger.mode==mode,320))Send("dangerMode",mode);
                }
                GUILayout.EndHorizontal();
                MvpText(HotelPresentation.DangerModeTerms(danger.mode),small);
                MvpReason(board);
            } else MvpText("Режим закреплён до следующей подготовки. Досрочный выход по тревоге — провал, а не победа.",small,muted);
            if(Fold("forecast","План аварий и условия зачёта")) {
            MvpSection("ПЛАН АВАРИЙ");
            foreach(var incident in danger.incidents.Take(danger.requiredIncidents))MvpText(HotelPresentation.DangerIncidentLabel(incident));
            if(danger.requiredIncidents==0)MvpText("Опасных аварий нет. Бытовая работа остаётся.",small,muted);
            MvpText("Сервис дают новое заселение и первое фактическое выполнение запроса. Повторные команды, компенсации и переселения очков не дают. Деньги за многодневное проживание не являются целью контракта.",small,muted);
            MvpText("Побед: "+danger.wins+" · провалов: "+danger.losses+" · серия: "+danger.streak+" · лучшая серия: "+danger.bestStreak,bold);
            if(MvpButton("Правила опасностей и спасения"))Game.Panel="briefing";
            }
        }
        void DangerOperations()
        {
            var danger=S.danger;
            if(HotelPresentation.DangerTerminal(S)) {
                MvpSection(HotelPresentation.DangerOutcomeName(danger.outcome));MvpText(danger.reason);
                if(MvpButton("Результат и следующий день",true,true))Game.Panel="summary";
            } else {MvpSection("КОНТРАКТ И БЕЗОПАСНОСТЬ");MvpText(HotelDangerRules.Objective(S),bold,gold);}
            MvpText("Безопасность: "+Mathf.CeilToInt(danger.safety)+" / 100 · аптечки: "+danger.medkits+" · аварийная самопомощь: "+danger.selfRescues);
            if(!danger.settled&&danger.status=="active"&&danger.safety<=0)
                MvpText("КРИТИЧЕСКОЕ ОКНО: "+HotelPresentation.Seconds(danger.criticalRemaining)+". Устраните источник или подайте тревогу у выхода.",bold,gold);
            foreach(var crew in danger.crew) {
                MvpSection(HotelPresentation.DangerCrewLabel(S,crew,Game.Session.LocalId));
                if(!crew.joined)continue;
                bool connected=S.players.Any(p=>HotelDangerRules.Slot(p.id)==crew.slot);
                MvpText((connected?"В отеле":"Вне сети · тело и состояние сохранены")+" · потерь сознания: "+crew.downs+" · спасений: "+crew.rescues,small,muted);
                if(crew.shield>0)MvpText("Защита после помощи: "+HotelPresentation.Seconds(crew.shield),small);
                if(!string.IsNullOrEmpty(crew.lastHit))MvpText("Последняя причина ранения: "+crew.lastHit,small,muted);
                if(crew.life=="dead")MvpText("Погиб до конца смены. Переподключение не лечит. Новая подготовка восстановит команду.",normal,gold);
                else if(crew.life=="downed"&&!danger.settled) {
                    bool self=crew.slot==HotelDangerRules.Slot(Game.Session.LocalId);
                    string target=(self?"recover_":"rescue_")+crew.slot;
                    MvpText(self?"Закройте панель и удерживайте E для самопомощи. Можно смотреть по сторонам; двигаться нельзя.":
                        "Подойдите к телу, освободите руки и удерживайте E. Тело остаётся доступным даже без подключения коллеги.",normal,gold);
                    MvpReason(HotelDangerRules.WorkError(S,Game.Session.LocalId,target));
                    if(self&&!danger.settled&&MvpButton("Вернуться к удержанию E"))Game.OpenPanel("");
                }
                else if(crew.life=="downed")MvpText("Смена завершена. Команду восстановит следующая подготовка.",normal,gold);
            }
            MvpSection("УГРОЗЫ · ИЗОЛЯЦИЯ → ИСТОЧНИК");
            foreach(var incident in danger.incidents.Take(danger.requiredIncidents)) {
                MvpText(HotelPresentation.DangerIncidentLabel(incident),bold,incident.status=="active"&&!incident.isolated?gold:paper);
                if(!danger.settled&&(incident.status=="warning"||incident.status=="active")) {
                    MvpText(incident.isolated?"Урон и рост остановлены. "+(incident.kind=="fumes"?"Швабра":"Ящик инструментов")+" → источник, удерживайте E.":
                        "Не входите в отмеченную зону. Свободные руки → отсекатель / вентиляция у двери → удерживайте E.");
                    if(incident.isolated)MvpText(incident.kind=="electric"?"Питание номера отключено до устранения источника.":incident.kind=="steam"?"Вода номера отключена до устранения источника.":"Вентиляция удерживает испарения; остаток нужно убрать.",small,muted);
                }
            }
            if(danger.requiredIncidents==0)MvpText("В режиме передышки опасных аварий нет.",small,muted);
            MvpText("Аптечная станция — западная часть лобби, рядом со складом. Пожарная тревога — справа от главного выхода: свободные руки + E; эвакуация проваливает контракт.",normal);
        }
        void DangerSummary()
        {
            var danger=S.danger;
            MvpSection(HotelPresentation.DangerOutcomeName(danger.outcome));
            MvpText(danger.reason,normal,danger.status=="failed"?gold:paper);
            // Terminal recovery is explicitly remote and life-independent; never ask a dead host to walk.
            string reason=HotelPresentation.DangerNextDayReason(S,Game.Session.IsHost);
            if(MvpButton("Восстановить команду · подготовка дня "+(S.day+1),reason=="",true)) {
                Send("nextday");if(S.phase=="preparation")Game.OpenPanel("management");
            }
            MvpReason(reason);
            MvpText("Начисленная премия: +"+danger.paidReward+" ₽ · расход на восстановление: −"+danger.chargedPenalty+" ₽",bold);
            MvpText("Выручка дня: "+S.earned+" ₽ · расходы: "+S.expenses+" ₽ · баланс: "+S.cash+" ₽",normal,S.cash<0?gold:paper);
            MvpText("Сервис: "+danger.servicePoints+" / "+danger.requiredService+" · аварии: "+danger.resolved+" / "+danger.requiredIncidents+" · время контракта: "+MvpTime(danger.elapsed));
            MvpText("Погибших: "+danger.crew.Count(c=>c.joined&&c.life=="dead")+" · без сознания: "+danger.crew.Count(c=>c.joined&&c.life=="downed"),bold,gold);
            foreach(var crew in danger.crew.Where(c=>c.joined))MvpText(HotelPresentation.DangerCrewLabel(S,crew,Game.Session.LocalId));
            MvpText("Побед: "+danger.wins+" · провалов: "+danger.losses+" · серия: "+danger.streak+" · рекорд: "+danger.bestStreak,bold);
            MvpText(danger.status=="failed"?"Гости эвакуированы; незавершённое проживание не считается успешным полным выездом. Контрактная премия потеряна.":
                "Продолжают проживание: "+S.guests.Count(g=>g.stage=="walking"||g.stage=="staying")+". Их договорённости и запросы сохраняются.");
            MvpText("Купленные номера и улучшения остаются. Подготовка доступна даже при долге и смерти хоста; подходить к стойке не нужно. Награда и расход уже учтены один раз.",normal,muted);
            MvpReviewsAndLedger();
        }
        void DangerHelp()
        {
            MvpSection("ОПАСНЫЕ КОНТРАКТЫ · ПОМОГАЙТЕ ДРУГ ДРУГУ");
            MvpText("В подготовке хост у доски выбирает передышку, обычный контракт или высокий риск. Открытие — у стойки. Учебная цепочка защищена; часы опасностей начнутся после её завершения или явного перехода к обычному темпу.");
            MvpText("До урона есть предупреждение. Электрическая дуга бьёт импульсами, горячая труба обжигает, едкие испарения вредят внутри отмеченной зоны. Обычная лужа не убивает.");
            MvpText("1. Освободите руки (Q). У двери опасного номера удерживайте E на отсекателе / вентиляции: "+HotelPresentation.Seconds(HotelDangerRules.IsolationSeconds)+". Изоляция остаётся включённой и останавливает урон и рост аварии.");
            MvpText("2. Принесите ящик инструментов к электрической дуге или трубе; к испарениям — швабру. Удерживайте E на источнике. Изоляция временно отключает связанную услугу, ремонт возвращает её. Бытовая грязь и поломки убираются отдельно.");
            MvpText("3. Коллега без сознания: освободите руки, подойдите к телу, наведитесь и удерживайте E "+HotelPresentation.Seconds(HotelDangerRules.RescueSeconds)+". Расходуется общая аптечка. Не успели до конца окна — сотрудник погибает до новой смены.");
            MvpText("Для себя: закройте панель и удерживайте E "+HotelPresentation.Seconds(HotelDangerRules.RecoverySeconds)+". Самопомощь расходует аптечку и общий аварийный запас; остатки — Дела → Команда, аптечки и аварии. Если помощь уже оказывает коллега, дождитесь его.");
            MvpText("Лечение ранений: аптечная станция в западной части лобби → свободные руки + E. Не тратьте общие аптечки без необходимости. Переподключение и загрузка не отменяют ранения или смерть.");
            MvpText("Безопасность отеля падает от запущенных угроз. При нуле видно последнее окно: устраните источник или подайте тревогу справа от выхода. Тревога требует свободных рук и E "+HotelPresentation.Seconds(HotelDangerRules.AlarmSeconds)+"; это провал без премии, но не смерть живой команды.",normal,gold);
            MvpText("Журнал НЕ останавливает опасности. Срочная угроза остаётся видна над страницей. TAB — журнал / результат, ESC — меню. Отпускание E, смена цели или открытие журнала отменяет работу. В настройках отключаются покачивание камеры и движения рук. Предупреждения сопровождаются текстом, обязательных вспышек нет.");
        }
        void MvpSummary()
        {
            if(Danger&&HotelPresentation.DangerTerminal(S)){DangerSummary();return;}
            MvpSection("РЕЗУЛЬТАТ СМЕНЫ");
            MvpText("Обслужено: "+S.served+" · выручка: "+S.earned+" ₽ · расходы: "+S.expenses+" ₽",bold);
            MvpText("Баланс: "+S.cash+" ₽ · репутация: "+S.mvp.reputation.ToString("0.0")+" / 5",title,S.cash<0?gold:green);
            MvpText("Продолжают проживание: "+S.guests.Count(g=>g.stage=="walking"||g.stage=="staying")+". Их договорённости, вещи и запросы переходят в новый день. Расчёт и отзыв — при завершении проживания.",normal);
            if(S.cash<0)MvpText("Отель может продолжить работу с небольшим долгом. Подготовьте доступные номера и получите оплату от гостей; необязательные покупки подождут.",normal,gold);
            string desk=MvpStation("desk",true);
            if(MvpButton("Подготовиться к дню "+(S.day+1),S.phase=="summary"&&desk=="",true)){Send("nextday");if(S.phase=="preparation")Game.Panel="management";}
            MvpReason(desk);MvpText("Пополнение и расходы учитываются один раз. Грязь и повреждения не исчезают после перехода.",small,muted);
            MvpReviewsAndLedger();
        }
        void MvpReviewsAndLedger()
        {
            MvpSection("ОТЗЫВЫ · ОЦЕНКА ОТ 1 ДО 5");
            if(S.mvp.reviews.Count==0)MvpText("Отзывов ещё нет.");
            foreach(var review in S.mvp.reviews.AsEnumerable().Reverse())MvpText(new string('★',Mathf.Clamp(review.stars,1,5))+" · день "+review.day+" · "+review.text);
            MvpSection("ЖУРНАЛ ОТЕЛЯ");foreach(string entry in S.ledger.AsEnumerable().Reverse())MvpText(entry,small,muted);
        }
        void MvpBriefing()
        {
            MvpSection("УПРАВЛЕНИЕ");
            MvpText("WASD — ходить   ·   Shift — быстрее   ·   E — взять / работать\nQ — положить   ·   F / средняя кнопка мыши — отметить для коллеги\nTAB — журнал   ·   ESC — меню. Мир не останавливается.");
            var hint=HotelOnboarding.GetHint(S,Game.Session.LocalId);
            if(hint!=null){MvpSection("СЕЙЧАС: "+hint.title);JournalHintBody(hint.body);}
            if(Danger&&Fold("danger-help","Опасности, спасение и провал смены"))DangerHelp();
            if(Fold("service-help","Как обслуживать отель")) {
            MvpText("Подготовка без таймера: бельё, полотенца, кофе, ремонт, прогноз и цены. Открытие и итоги — у стойки, покупки — у доски. Обычная смена длится "+Mathf.RoundToInt(S.dayLength/60)+" минут.");
            MvpText("Выберите гостя явно и проверьте требования к вместимости и качеству. Семью представляет один персонаж за двух человек. По запросам доставляйте полотенца и кофе, убирайте комнату и возвращайте собственный багаж гостя.");
            MvpText("Tab открывает общий обзор. F или средняя кнопка мыши отмечает объект для коллеги на 6 секунд. Просмотр панели освобождает мышь, но не останавливает отель.");
            }
            if(Fold("pace-help","Обучение и темп смены")) {
            MvpSection(HotelDirector.Status(S));MvpText(HotelMvpDirector.Status(S));
            MvpText("Подсказки и учебный темп настраиваются отдельно. Вводный режим ждёт освоения регистрации, багажа, полотенца, белья и первой протечки.",small,muted);
            if(S.day<=2&&MvpButton(S.tutorialSkipped?"Показать подсказки":"Скрыть подсказки",Game.Session.IsHost&&HotelDangerRules.CanAct(S,Game.Session.LocalId)))Send(S.tutorialSkipped?"resumeTutorial":"skipTutorial");
            if(HotelDirector.IsGuided(S)&&MvpButton("Перейти к обычному темпу…",Game.Session.IsHost&&HotelDangerRules.CanAct(S,Game.Session.LocalId)))Game.Panel="pace-confirm";
            }
        }
        void MvpFinishConfirmation()
        {
            MvpSection("ПОДВЕСТИ ИТОГИ ДНЯ?");
            if(Danger) {
                MvpText(HotelDangerRules.Objective(S),bold,gold);
                MvpText(S.danger.mode=="relief"?"Передышка заканчивается без контрактной премии и без зачёта победы.":
                    "Победа требует всех целей, минимального времени и живой команды без потерявших сознание. Хост проверит условия при завершении; невыполненный контракт нельзя выдать за победу.");
                MvpText("Нужно прекратить риск? Физическая тревога у выхода: свободные руки + удержание E. Это эвакуация с провалом, без премии и с расходом "+S.danger.penalty+" ₽.",normal,gold);
            }
            bool failedClose=Danger&&S.phase=="closing"&&S.danger.mode!="relief"&&!HotelDangerRules.GoalsMet(S);
            MvpText(failedClose?"Цели не выполнены. Подтверждение означает ПРОВАЛ: без премии, восстановление −"+S.danger.penalty+" ₽. Неоплаченные проживания будут прерваны без полной оплаты, включая многодневные.":
                "В очереди: "+S.guests.Count(g=>g.stage=="queue")+". При успешном завершении гости с завершением проживания будут рассчитаны; многодневные гости останутся.");
            MvpText("Неисполненные запросы и недоставленный багаж влияют на отзыв. Грязь и поломки сохранятся. После итогов можно начать подготовку следующего дня.");
            if(MvpButton("Продолжить обслуживание"))Game.Panel="management";
            string reason=MvpStation("desk",true);
            if(MvpButton(failedClose?"Подтвердить провал и эвакуацию":Danger?"Проверить цели и завершить смену":"Завершить смену",reason==""&&(S.phase=="open"||S.phase=="closing"),true)){Send("finish");if(S.phase=="summary")Game.Panel="summary";}
            MvpReason(reason);
        }
        void DangerAbandonConfirmation()
        {
            MvpSection("ОТКАЗ ОТ КОНТРАКТА · НЕКОМУ ПРОДОЛЖАТЬ");
            MvpText("Хост недееспособен, а подключённых сотрудников, способных продолжить работу, нет. Можно ждать возвращения коллеги либо признать провал.");
            MvpText("Премия не начисляется. Расход на восстановление: "+S.danger.penalty+" ₽. Неоплаченные проживания прерываются без полной оплаты. Покупки сохраняются; следующая подготовка восстановит команду.",normal,gold);
            if(MvpButton("Ждать коллегу / вернуться"))Game.OpenPanel("");
            if(MvpButton("Подтвердить провал контракта",Game.Session.IsHost&&HotelDangerRules.HostCanForfeit(S),true)) {
                Send("abandonDanger");if(S.phase=="summary")Game.Panel="summary";
            }
        }
        void MvpRefuseConfirmation()
        {
            var guest=S.guests.Find(g=>g.id==refusalGuest);
            var reservation=S.mvp.reservations.Find(r=>r.id==refusalReservation);
            bool available=refusalGuest>0?guest?.stage=="queue":reservation?.status=="confirmed";
            MvpSection(refusalGuest>0?"ГОСТЬ: "+(guest?.name??"уже ушёл"):"БРОНЬ: "+refusalReservation);
            if(reservation!=null)MvpText(HotelPresentation.GuestContract(reservation.contract));
            MvpText("Отказ освобождает место для других гостей и сохраняется в состоянии отеля. Проверьте условия перед подтверждением.");
            if(!available)MvpText("Состояние уже изменилось. Отказ сейчас недоступен.",normal,gold);
            string reason=MvpStation("desk");
            if(MvpButton("Подтвердить отказ",available&&reason=="",true))
            {
                if(refusalGuest>0)MvpGuestSend("refuse",refusalGuest);else Send("refuse",refusalReservation);
                Game.Panel=refusalGuest>0?"guests":"schedule";
            }
            MvpReason(reason);if(MvpButton("Сохранить гостя / бронь"))Game.Panel=refusalGuest>0?"guests":"schedule";
        }
        static string MvpTime(float seconds){int value=Mathf.Max(0,Mathf.RoundToInt(seconds));return (value/60).ToString("00")+":"+(value%60).ToString("00");}
        static string MvpComplaintName(string category)
        {
            switch(category){case "dirt":case "cleanliness":case "cleaning":return "Чистота";case "equipment":return "Оборудование";case "toilet":return "Туалет / вода";case "tv":case "lamp":return "ТВ / освещение";case "towel":return "Полотенца";case "waiting":case "queue":return "Ожидание";case "luggage":return "Багаж";case "noise":return "Шум";case "coffee":return "Кофе";case "water":return "Вода";case "power":return "Электричество";default:return category;}
        }
        string MvpTargetName(string target)
        {
            if(string.IsNullOrEmpty(target))return "отель";
            if(Danger&&HotelDangerRules.IsWorkTarget(target))return HotelPresentation.DangerTargetName(S,target);
            switch(target){case "desk":return "Ресепшен";case "board":return "Доска управления";case "coffee":return "Кофейная станция";case "utility_water":return "Общая вода";case "utility_power":return "Электрощит";case "linen":return "Чистое бельё";case "towels":return "Полотенца";case "hamper":return "Приёмник белья";case "bin":return "Мусорный бак";case "tools":return "Инструменты";}
            string[] parts=target.Split('_');
            if(parts.Length==2&&int.TryParse(parts[1],out int room)) {
                string kind=parts[0];string name=kind=="bed"?"Кровать":kind=="door"?"Номер":kind=="water"?"Лужа":kind=="clean"?"Уборка":kind=="dirtytowel"?"Грязные полотенца":kind=="trash"?"Мусор":kind=="bag"||kind=="luggage"?"Багаж":kind=="guest"?"Гость":kind=="towel"?"Полотенце":kind=="coffee"?"Кофе":HotelPresentation.EquipmentName(kind);
                return name+" · "+room;
            }
            return target;
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
            Frame("Перевести дух",Game.Session.PlaytestSlot?"Отдельный тестовый слот · основной отель не затронут. Мир продолжает жить.":"Меню не ставит мир на паузу. Учебный таймер ждёт освоения основ; работа и движение продолжаются.");
            if(Button(new Rect(320,305,800,58),"Вернуться в отель",true,true))Game.OpenPanel("");
            if(Button(new Rect(320,379,800,52),"Настройки камеры и звука"))Game.OpenPanel("settings");
            bool forfeit=Danger&&Game.Session.IsHost&&HotelDangerRules.HostCanForfeit(S);
            if(Button(new Rect(320,449,800,52),forfeit?"Некому продолжать: признать провал…":Danger&&HotelPresentation.DangerTerminal(S)?"Результат смены / следующий день":"Журнал дежурного"))Game.OpenPanel(forfeit?"abandon-confirm":Danger&&HotelPresentation.DangerTerminal(S)?"summary":"tasks");
            if(Button(new Rect(320,519,800,52),"Сохранить отель",Game.Session.IsHost)){if(Game.Session.Save())Game.Notify("Сохранено.");}
            if(Button(new Rect(320,586,800,48),"Помощь и управление"))Game.OpenPanel("briefing");
            if(Button(new Rect(320,650,800,45),Game.Session.IsHost?"Сохранить и закрыть сессию":"Покинуть сессию"))Game.Leave();
            Text(new Rect(320,710,545,45),Game.Session.Status+"\nСохранение хранится на компьютере хоста.",small,muted);
            if(Game.Session.SteamMode && Button(new Rect(875,710,240,40),"Скопировать ID лобби")){GUIUtility.systemCopyBuffer=HotelSteam.LobbyId.ToString();Game.Notify("ID лобби скопирован.");}
        }
        void Send(string action,string target="",int number=0,int guestId=0){Game.Session.Send(new HotelCommand(action,target,number){guestId=guestId});}
        static string Phase(string phase){switch(phase){case "preparation":return "ПОДГОТОВКА";case "open":return "ОТЕЛЬ ОТКРЫТ";case "closing":return "ЗАКРЫТИЕ";case "summary":return "ИТОГИ";default:return phase;}}
    }
}
