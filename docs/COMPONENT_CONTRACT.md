# Контракт параллельной разработки pre-MVP

Общий namespace `WorstHotel`. Unity 6000.3.2f1; C#; URP 17.3; NGO 2.7.0. Общие DTO и координаты в Assets/Scripts/HotelTypes.cs, не менять без согласования с интегратором. Единицы денег целые. Игрок стоит на y≈0, голова 1.65. Номера 101/103 слева, 102/104 справа. Коридор x±1.5 z2–16; лобби x±7.8 z−7–2. RoomCenter/Target являются авторитетными координатами взаимодействий.

## Игровая логика — отдельная задача

Файлы: Assets/Scripts/HotelSimulation.cs, HotelSaveStore.cs, Assets/Editor/HotelSimulationTests.cs. Никаких NGO, UI или создания сцен. Pure-state логика на DTO, допускается UnityEngine.Vector3/Mathf/JsonUtility.

Обязательное API:

- `new HotelSimulation(HotelState state = null)`; `public HotelState State { get; }`.
- `void Join(ulong id)`; `void Leave(ulong id)`; `void Tick(float dt)`.
- `string Execute(ulong playerId, HotelCommand command)` возвращает пустую строку при успехе, иначе понятную причину. Неуспешная команда ничего не расходует.
- `List<string> Tasks()` — актуальные задачи на русском из фактического мира.
- `HotelSaveStore.Save(HotelState state, string path)`; `HotelState HotelSaveStore.Load(string path)`; атомарность/backup/версия/проверка структуры; сброс подключенных игроков и безопасное освобождение held-объектов после load.
- `HotelSimulationTests.RunAll()` возвращает `List<string>` с названиями успешных тестов и бросает exception при провале; интегратор вызовет в Unity batch.

Команды: pose (position/yaw/pitch), interact(target), pickup(target=item ID), drop(position), beginwork(target), cancelwork, heartbeat(target), checkin(number=room), checkout(number=guestID), compensate(number=guestID), open, finish, nextday, upgrade(target=toolbox|beds|cart), toggleRoom(number=room). Host-only административные действия id=0. Административные команды desk/board проверять расстояние. Все обычные взаимодействия проверять по расстоянию до Target/Item; work = server-timed, heartbeat удерживаемой E. Запрет двойного владельца/оплаты/покупки. Длительность дня 480 сек, 3–4 гостя. Два игрока, мягкое восстановление, конечные запасы с междневным пополнением. Состояния гостя queue/walking/staying/checkout/leaving/gone. Помещения подготавливаются: dirty bed → dirtylinen в руку → hamper, clean linen → bed; trash → trashbag → bin; towel → towel stand; toolbox → sink repair; mop → wet floor cleanup. Bag ownerGuest не равен holder. Интеракции с desk и board обрабатывает UI; не симулятор. NPC ходят по маршруту queue→центр коридора→дверь→центр комнаты и обратно, без проходов сквозь стены. Динамическая навигация не блокирует этот pre-MVP.

## Мир и визуал — отдельная задача

Файлы: Assets/Scripts/HotelWorld.cs и собственные shaders/materials в Assets/Art при необходимости. Не менять DTO, пакеты, сцены, project settings. Нет сторонних ресурсов без лицензии.

API: `public class HotelWorld : MonoBehaviour` с `public void Build()` и `public void Apply(HotelState state, ulong localPlayerId)`; `public static GameObject MakeItem(string kind)` и `public static GameObject MakePerson(bool employee, int seed)` для first-person carry и NPC. Созданные экземпляры под world root. В Build только статический отель, декор, свет. Apply обновляет динамических NPC/других игроков/предметы/состояния комнат, не уничтожая всё каждый кадр. Тело локального игрока скрывать. Held предмет локального игрока скрыт в мире (интегратор создаёт видимый carry). Item collider у held отключён. Все interactable collider objects несут компонент `HotelTarget` c public string `id` и `label`; его объявить здесь. Статические id: desk/board/linen/towels/hamper/bin/bed_101/sink_101/water_101/towel_101/trash_101/bag_101/door_101 (аналогично 102–104). Item target id = ItemState.id. Guest target id = guest_ID. Не делать непрозрачную декоративную дверь, закрывающую рабочий проход. Реальные коллайдеры стен/мебели, проёмы 1.5 м, стыки закрыты. Screen-space UI и камера у интегратора. Стилизованный low-poly отель тёплого дерева/кремовой штукатурки/бордовых акцентов, читаемые материалы и освещение, оригинальная выразительная геометрия (не просто пустые кубы). Все надписи через TextMesh (Cyrillic font поддержка), использовать встроенный LegacyRuntime.ttf. Обязательные shaders обеспечить Resources или сериализованными материалами, иначе Shader.Find ломается в build.

## Интегратор (основная задача)

FP input/camera/carry, NGO/transport/session, UI, boot, builder/CLI, end-to-end smoke, финальный EXE. Чужие задачи работают в отдельных git worktree и сообщают commit hash + изменённые файлы + реальные проверки. Не запускать Unity editor одновременно без координации (одна машина). Можно компилировать C# локальным dotnet с Unity ссылками. Отчёты сохранять в docs/reports/ по собственной теме.
