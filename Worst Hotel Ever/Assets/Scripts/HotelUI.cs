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
        readonly Color ink=new Color(.095f,.135f,.14f), paper=new Color(.96f,.93f,.85f), muted=new Color(.62f,.69f,.65f), red=new Color(.68f,.24f,.22f), gold=new Color(.9f,.68f,.36f), green=new Color(.35f,.71f,.53f);
        string address="127.0.0.1",port="7777",password="";
        Vector2 scroll; string lastPhase="";
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
                else Window();
            }
            if(Game.Toast!=""&&Game.Playing) {
                Box(new Rect(370,115,700,60),new Color(.07f,.12f,.13f,.94f));Text(new Rect(389,126,662,45),Game.Toast,normal);
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
            Text(new Rect(49,837,465,30),"PRE-MVP 0.1  /  WINDOWS  /  1–2 СОТРУДНИКА",small,muted);
            Text(new Rect(1020,35,365,50),"ОТЕЛЬ «ПОЧТИ ГРАНД»\n★  НАЧНИТЕ С ЧИСТОГО ПОЛОТЕНЦА",small,paper);
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
                Text(new Rect(50,489,460,85),"Начать новый отель?\nТекущий слот будет заменён с резервной копией.",normal);
                if(Button(new Rect(50,600,460,57),"Да, начать новую историю",true,true)){Game.Session.Host(false,7777,"");Game.OpenPanel("");}
                if(Button(new Rect(50,674,460,52),"Вернуться"))Game.Panel="menu";
            } else {
                bool saved=System.IO.File.Exists(Game.Session.SavePath);
                if(Button(new Rect(50,493,460,58),"Продолжить отель",saved,true)) {Game.Session.Disconnect(false);Game.Session.Host(true,7777,"");Game.OpenPanel("");}
                if(Button(new Rect(50,564,460,58),"Создать новый отель")) {
                    if(saved)Game.Panel="new";else{Game.Session.Disconnect(false);Game.Session.Host(false,7777,"");Game.OpenPanel("");}
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
            Text(new Rect(730,36,220,28),S.phase=="open"?"ДО ЗАКРЫТИЯ  "+((int)left/60).ToString("00")+":"+((int)left%60).ToString("00"):"ВЫДОХНИТЕ. ПОКА.",small,gold);
            Text(new Rect(1080,34,290,38),S.cash+" ₽",title,green);
            if(Game.Panel!="")return;
            Box(new Rect(716,446,8,8),paper);
            if(Game.FocusLabel!="") {
                Box(new Rect(430,674,580,58),new Color(.065f,.105f,.11f,.94f));
                Box(new Rect(444,686,32,32),gold);Text(new Rect(451,687,28,30),"E",bold,ink);
                Text(new Rect(490,687,505,38),Game.FocusLabel,normal);
            }
            if(Game.LocalPlayer!=null && Game.LocalPlayer.workTarget!="") {
                Box(new Rect(530,742,380,7),new Color(.2f,.28f,.27f));
                Box(new Rect(530,742,380*Mathf.Clamp01(Game.LocalPlayer.workProgress),7),gold);
                Text(new Rect(580,756,400,25),"Удерживайте E до завершения",small);
            }
            Box(new Rect(25,797,730,77),new Color(.065f,.105f,.11f,.90f));
            Text(new Rect(42,808,698,28),Game.Held==null?"Руки свободны. Самое время помочь коллеге.":"В руках: "+HotelGame.ItemName(Game.Held.kind)+"   ·   Q — положить",normal);
            Text(new Rect(42,845,698,23),"WASD — ходить   SHIFT — быстрее   TAB — задачи   ESC — меню",small,muted);
            Text(new Rect(1140,832,260,42),"Автосохранение: "+(Game.Session.IsHost?Game.Session.LastSaved:"у хоста")+"\n"+(int)Game.FPS+" FPS",small,muted);
            var task=TaskLines().FirstOrDefault();
            if(task!=null){Box(new Rect(25,125,320,96),new Color(.065f,.105f,.11f,.87f));Text(new Rect(40,137,290,22),"С ЧЕГО НАЧАТЬ",small,gold);Text(new Rect(40,165,290,50),task,normal);}
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
            Frame(panel=="tasks"?"Работа найдётся каждому":panel=="reception"?"Ресепшен":panel=="summary"?"Смена окончена":"Управление отелем", "Мир продолжает жить, пока открыта панель.  /  ESC — вернуться в игру");
            if(panel=="summary") { Summary(); return; }
            if(Button(new Rect(202,258,220,42),"Номера / гости"))Game.Panel="reception";
            if(Button(new Rect(434,258,220,42),"Задачи"))Game.Panel="tasks";
            if(Button(new Rect(666,258,220,42),"Смена / улучшения"))Game.Panel="management";
            Box(new Rect(202,314,1036,2),new Color(.24f,.31f,.29f));
            if(panel=="tasks") { Tasks(); return; }
            if(panel=="management") { Management(); return; }
            Rooms();
        }
        void Rooms()
        {
            var queue=S.guests.FirstOrDefault(g=>g.stage=="queue");
            Text(new Rect(202,334,485,52),queue!=null?"В очереди: "+queue.name+"  /  "+queue.kind:"Никто не ждёт заселения",bold,gold);
            int y=390;
            foreach(var room in S.rooms) {
                Box(new Rect(202,y,505,71),new Color(.13f,.20f,.20f));
                var guest=S.guests.Find(g=>g.id==room.guestId);
                Text(new Rect(216,y+9,320,28),"№ "+room.number+"  ·  "+(room.guestId!=0?guest?.name??"Занят":room.outOfService?"Закрыт":"Свободен"),bold);
                Text(new Rect(216,y+39,325,27),RoomProblems(room),small,room.bed==2&&!room.leak?muted:gold);
                if(Button(new Rect(543,y+13,150,43),"Заселить",queue!=null&&room.guestId==0&&!room.outOfService))Send("checkin","",room.number);
                y+=83;
            }
            Text(new Rect(746,334,445,37),"ВЫЕЗДЫ И ВПЕЧАТЛЕНИЯ",bold);
            y=384;
            foreach(var guest in S.guests.Where(g=>g.stage!="gone"&&g.stage!="leaving").Take(4)) {
                Text(new Rect(746,y,455,27),guest.name+"  ·  "+(guest.room==0?"ожидает":"№ "+guest.room),bold);
                Text(new Rect(746,y+29,460,42),GuestStatus(guest),small,guest.satisfaction<45?gold:muted);
                if(guest.stage=="checkout") {if(Button(new Rect(746,y+67,207,37),"Принять оплату"))Send("checkout","",guest.id);}
                else if(guest.room!=0 && !guest.compensated) {if(Button(new Rect(746,y+67,207,37),"Компенсация"))Send("compensate","",guest.id);}
                y+=118;
            }
        }
        string RoomProblems(RoomState r)
        {
            string value=""; if(r.bed!=2)value+=r.bed==1?"Грязное бельё · ":"Нет белья · ";
            if(!r.towel)value+="Нет полотенца · ";if(r.trash)value+="Мусор · ";if(r.leak)value+="Течь · ";if(r.water>.05f)value+="Мокрый пол · ";
            return value==""?"Всё готово к приёму гостя":value.TrimEnd(' ','·');
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
            var lines=new System.Collections.Generic.List<string>();
            if(S==null)return lines;
            if(S.phase=="preparation")lines.Add("Откройте смену у стойки ресепшена.");
            foreach(var g in S.guests) {
                if(g.stage=="queue")lines.Add("Заселить: "+g.name+" — стойка ресепшена.");
                if(g.room!=0 && !g.luggageDelivered && (g.stage=="walking"||g.stage=="staying"))lines.Add("№ "+g.room+": доставить чемодан "+g.name+" на багажную подставку.");
                if(g.stage=="checkout")lines.Add("Принять выезд: "+g.name+" — ресепшен.");
            }
            foreach(var r in S.rooms) {
                if(r.leak)lines.Add("№ "+r.number+": починить раковину набором инструментов (удерживать E).");
                if(r.water>.05f)lines.Add("№ "+r.number+": убрать воду шваброй (удерживать E).");
                if(!r.towel)lines.Add("№ "+r.number+": принести чистое полотенце со склада.");
                if(r.bed==1)lines.Add("№ "+r.number+": снять грязное бельё, отнести в корзину.");
                if(r.bed==0)lines.Add("№ "+r.number+": заправить кровать чистым бельём со склада.");
                if(r.trash)lines.Add("№ "+r.number+": собрать мусор и вынести в бак на складе.");
            }
            if(S.phase=="closing")lines.Add("Завершите выезды, затем подведите итоги у стойки.");
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
            if((S.phase=="open"||S.phase=="closing") && Button(new Rect(202,470,477,55),"Закончить смену",Game.Session.IsHost,true))Send("finish");
            if(S.phase=="summary" && Button(new Rect(202,470,477,55),"К итогам дня"))Game.Panel="summary";
            if(Button(new Rect(202,543,232,47),"Сохранить",Game.Session.IsHost)){Game.Session.Save();Game.Notify("Отель сохранён.");}
            if(Button(new Rect(447,543,232,47),"Задачи"))Game.Panel="tasks";
            Text(new Rect(202,615,477,109),"Управление сменой и покупки — у хоста. Подойдите к ресепшену или доске управления.\nВ подготовке время и новые проблемы не идут.",normal,muted);
            Text(new Rect(729,336,477,38),"ПЕРВЫЕ УЛУЧШЕНИЯ",bold,gold);
            Upgrade(729,397,"Второй набор инструментов","Чтобы коллега тоже мог чинить.","toolbox",S.secondToolbox);
            Upgrade(729,506,"Хорошие матрасы","Гостям будет немного уютнее.","beds",S.betterBeds);
            Upgrade(729,615,"Багажная тележка","Меньше таскать, больше успевать.","cart",S.cartUpgrade);
        }
        void Upgrade(float x,float y,string titleText,string sub,string id,bool owned)
        {
            Text(new Rect(x,y,475,30),titleText,bold);Text(new Rect(x,y+33,475,30),sub,small,muted);
            if(Button(new Rect(x,y+64,470,36),owned?"Уже куплено":"Купить",!owned&&Game.Session.IsHost&&S.phase=="preparation"))Send("upgrade",id);
        }
        void Summary()
        {
            Text(new Rect(204,285,520,66),S.served+" гостей обслужено",title,gold);
            Text(new Rect(204,369,470,140),"Выручка: "+S.earned+" ₽\nРасходы: "+S.expenses+" ₽\nНа счету: "+S.cash+" ₽",title);
            Text(new Rect(750,285,458,37),"ЧТО О НАС ГОВОРЯТ",bold,gold);
            int y=337;foreach(string review in S.reviews.AsEnumerable().Reverse().Take(4)){Text(new Rect(750,y,450,77),"«"+review+"»",normal);y+=88;}
            if(Button(new Rect(204,610,470,59),"Подготовиться к дню "+(S.day+1),Game.Session.IsHost,true)) {Send("nextday");Game.Session.Save();if(S.phase=="preparation")Game.OpenPanel("management");}
            Text(new Rect(204,700,1000,45),"Отель сохраняется автоматически у хоста. Уборка и повреждения не исчезнут после перезапуска.",small,muted);
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
            if(Button(new Rect(230,609,450,48),"Инверсия Y: "+(Game.InvertY?"вкл":"выкл")))Game.InvertY=!Game.InvertY;
            if(Button(new Rect(704,609,476,48),"Покачивание камеры: "+(Game.Bob?"вкл":"выкл")))Game.Bob=!Game.Bob;
            if(Button(new Rect(230,696,950,49),"Сохранить настройки",true,true)){Game.StoreSettings();Game.OpenPanel(Game.Playing?"pause":"menu");}
        }
        void Pause()
        {
            Frame("Перевести дух","В онлайн-сессии время не останавливается. Для спокойной уборки завершите смену.");
            if(Button(new Rect(320,305,800,58),"Вернуться в отель",true,true))Game.OpenPanel("");
            if(Button(new Rect(320,379,800,52),"Настройки камеры и звука"))Game.OpenPanel("settings");
            if(Button(new Rect(320,449,800,52),"Задачи и состояние номеров"))Game.OpenPanel("tasks");
            if(Button(new Rect(320,519,800,52),"Сохранить отель",Game.Session.IsHost)){Game.Session.Save();Game.Notify("Сохранено.");}
            if(Button(new Rect(320,608,800,52),Game.Session.IsHost?"Сохранить и закрыть сессию":"Покинуть сессию"))Game.Leave();
            Text(new Rect(320,691,800,50),Game.Session.Status+"\nСохранение хранится локально на компьютере хоста.",small,muted);
        }
        void Send(string action,string target="",int number=0){Game.Session.Send(new HotelCommand(action,target,number));}
        static string Phase(string phase){switch(phase){case "preparation":return "ПОДГОТОВКА";case "open":return "ОТЕЛЬ ОТКРЫТ";case "closing":return "ЗАКРЫТИЕ";case "summary":return "ИТОГИ";default:return phase;}}
    }
}
