using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using VRageMath;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using Sandbox.ModAPI.Interfaces;
using Sandbox.ModAPI.Ingame;
using Sandbox.Game.EntityComponents;
using VRage.Game.Components;
using VRage.Collections;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Data;
using Sandbox.Game.GameSystems;
using System.CodeDom;
using System.Collections.ObjectModel;
using Sandbox.Game.Debugging;
using VRage;
using Sandbox.Definitions;
using System.Diagnostics;
using Sandbox.Game;
using VRage.Game.Entity;
using Sandbox.Game.Entities;
using VRage.ObjectBuilders;
//using Sandbox.ModAPI;
//using VRage.Game.ModAPI;


namespace Template {

    public sealed class Program : MyGridProgram {
        #region Copy



        /** InputData - статический класс, хранящий входные значения скрипта. Здесь необходимо менять названия блоков
         * 
         */
        static class InputData {
            // Имя панели ввода
            static public string InputPanelName { get; private set; } = "Input Panel 1";

            // Имя панели вывода
            static public string OutputPanelName { get; private set; } = "Output Panel 1";

            //  Имя отправного контейнера
            static public List<string> SupplyContainerNames { get; private set; } = new List<string>
            { "Supply Container 1", "Supply Container 2", "Supply Container 3" };

            // Имя конечного контейнера
            static public string DestinationContainerName { get; private set; } = "Destination Container 1";

            // Имя сборщика
            static public string AssemblerName { get; private set; } = "Private Assembler 1";
        }

        class TransferItem {
            public string SubtypeId { get; private set; }

            public long TransferRequestedAmount { get; set; }

            public bool IsAssembleRequested { get; set; }

            public TransferItem() {
                SubtypeId = "";
                TransferRequestedAmount = -1;

                IsAssembleRequested = false;
            }

            public void setData(string subtypeId, long amount, bool isAssembleRequested) {

                SubtypeId = subtypeId;
                TransferRequestedAmount = amount;

                IsAssembleRequested = isAssembleRequested;
            }

        }

        public enum SmartTransferResult {

            Succesful,

            InputDataError,

            AssemblerError,

            TransferError,

            NotEnoughtComponents

        }

        // Класс перемещения предметов из одного инвентаря в другой инвентарь
        class SmartItemTransferer {

            // Метод перемещения предметов с заказом крафта, если предметов не достаточно
            public SmartTransferResult smartTransferTo(List<IMyInventory> supplyStorage, IMyInventory destinationInventory, List<TransferItem> transferItems, AssemblerManager asmManager) {

                // if input data is invalid than return input data error
                if (supplyStorage.Count <= 0 || destinationInventory == null || transferItems.Count <= 0 || asmManager == null) return SmartTransferResult.InputDataError;


                foreach (TransferItem transferItem in transferItems) {

                    // if item isn't be transfered
                    if (transferItem.TransferRequestedAmount <= 0) continue;


                    // if not enought items in storage for make transfer
                    if (calculateAvailableItemAmount(supplyStorage, transferItem.SubtypeId) < transferItem.TransferRequestedAmount) {

                        // if item isn't requested for assemble
                        if (!transferItem.IsAssembleRequested) {
                            // on assemble request flag for item
                            transferItem.IsAssembleRequested = true;

                            // if assemble isn't made than return assemble error
                            if (!asmManager.assembleComponent(transferItem.SubtypeId, transferItem.TransferRequestedAmount)) return SmartTransferResult.AssemblerError;

                        }

                        // save snapshot
                        SmartItemTransfererSnapshot.saveSnapshot(this, new SmartItemTransfererSnapshot.SmartTransferToSnapshot(supplyStorage, destinationInventory, transferItems, asmManager));

                        return SmartTransferResult.NotEnoughtComponents;
                    }

                    SmartTransferResult transferResult = multiTransferTo(supplyStorage, destinationInventory, transferItem);

                    if (transferResult != SmartTransferResult.Succesful) return transferResult;

                }

                return SmartTransferResult.Succesful;
            }

            static private long calculateAvailableItemAmount(List<IMyInventory> inventories, string itemSubtypeID) {

                long availableItemAmount = 0;

                foreach (IMyInventory inventory in inventories) {

                    List<MyInventoryItem> inventoryItems = new List<MyInventoryItem>();
                    inventory.GetItems(inventoryItems);
                    foreach (MyInventoryItem item in inventoryItems) {

                        if (item.Type.SubtypeId == itemSubtypeID) availableItemAmount += item.Amount.RawValue;

                    }

                }

                return availableItemAmount;
            }

            static private SmartTransferResult multiTransferTo(List<IMyInventory> supplyStorage, IMyInventory destinationInventory, TransferItem transferItem) {

                foreach (IMyInventory supplyInventory in supplyStorage) {

                    // get inventory items
                    List<MyInventoryItem> supplyItems = new List<MyInventoryItem>();
                    supplyInventory.GetItems(supplyItems);

                    foreach (MyInventoryItem supplyItem in supplyItems) {

                        // if subtype isn't equals than skip that item
                        if (supplyItem.Type.SubtypeId != transferItem.SubtypeId) continue;


                        // if items amount >= item amount for transfer
                        if (supplyItem.Amount.RawValue >= transferItem.TransferRequestedAmount) {

                            // init MyFixedPoint from item amount for transfer (long) 
                            MyFixedPoint fp = new MyFixedPoint();
                            fp.RawValue = transferItem.TransferRequestedAmount;

                            // if transfer isn't made than return transfer error
                            if (!supplyInventory.TransferItemTo(destinationInventory, supplyItem, fp)) return SmartTransferResult.TransferError;

                            // reset item amount for transfer
                            transferItem.TransferRequestedAmount = 0;
                        }

                        // if items amount < item amount for transfer
                        else if (supplyItem.Amount.RawValue < transferItem.TransferRequestedAmount) {

                            // if transfer isn't made than return transfer error
                            if (!supplyInventory.TransferItemTo(destinationInventory, supplyItem)) return SmartTransferResult.TransferError;

                            // reduce item amount for transfer to transfered item amount
                            transferItem.TransferRequestedAmount -= supplyItem.Amount.RawValue;
                        }
                    }
                }

                return SmartTransferResult.Succesful;
            }

        }


        static class Worker {

            // Грид система терминала
            public static IMyGridTerminalSystem GridTerminalSystem { get; set; }

            // Состояние работы
            public static WorkStates actualWorkState { get; set; } = Worker.WorkStates.WaitingStart;

            public static void resetWorkState() {
                Worker.actualWorkState = Worker.WorkStates.WaitingStart;
            }

            private static List<IMyInventory> getSupplyStorage(List<string> supplyStorageContainerNames) {
                
                // init inventory storage
                List<IMyInventory> supplyStorage = new List<IMyInventory>();

                foreach (string supplyContainerName in InputData.SupplyContainerNames) {

                    // get block
                    IMyTerminalBlock block = GridTerminalSystem.GetBlockWithName(supplyContainerName);

                    // if block is null then we can't find it and return null 
                    if (block == null) return null;

                    // add block inventory to storage
                    supplyStorage.Add(block.GetInventory());
                }

                return supplyStorage;
            }

            private static void doTransfer(SmartItemTransferer smartTransferer, List<IMyInventory> supplyStorage, IMyInventory destinationInventory, List<TransferItem> transferItems, AssemblerManager asmManager) {

                switch (smartTransferer.smartTransferTo(supplyStorage, destinationInventory, transferItems, asmManager)) {

                    case SmartTransferResult.Succesful: {
                            PanelWriter.writeOutputDataLine("Перенос предметов успешно завершен.", true);
                            Worker.actualWorkState = Worker.WorkStates.Completed;
                            break;
                        }

                    case SmartTransferResult.NotEnoughtComponents: {
                            Worker.actualWorkState = Worker.WorkStates.WaitingResources;
                            break;
                        }

                    case SmartTransferResult.InputDataError: {
                            PanelWriter.writeOutputDataLine("Перенос предметов прерван. Причина: Ошибка входных данных", true);
                            Worker.actualWorkState = Worker.WorkStates.Aborted;
                            break;
                        }

                    case SmartTransferResult.AssemblerError: {
                            PanelWriter.writeOutputDataLine("Перенос предметов прерван. Причина: Ошибка сборщика предметов", true);
                            Worker.actualWorkState = Worker.WorkStates.Aborted;
                            break;
                        }

                    case SmartTransferResult.TransferError: {
                            PanelWriter.writeOutputDataLine("Перенос предметов прерван. Причина: Ошибка переноса предметов", true);
                            Worker.actualWorkState = Worker.WorkStates.Aborted;
                            break;
                        }

                    default: {
                            PanelWriter.writeOutputDataLine("Перенос предметов прерван. Причина: Неизвестная ошибка", true);
                            Worker.actualWorkState = Worker.WorkStates.Aborted;
                            break;
                        }
                }

            }

            // Методы выполнения работы ( перемещение ресурсов по запросу ) 
            public static void work() {

                // Устанавливаем флаг, что мы начали работу
                Worker.actualWorkState = WorkStates.Processing;

                // get supply storage
                List<IMyInventory> supplyStorage = Worker.getSupplyStorage(InputData.SupplyContainerNames);

                // if supply storage is null then abort work
                if (supplyStorage == null) {
                    PanelWriter.writeOutputDataLine("Перенос предметов прерван. Причина: Ошибка данных хранилища припасов", true);
                    Worker.actualWorkState = WorkStates.Aborted; 
                }

                // Берем инвентарь назначения
                IMyInventory destinationInventory = Worker.GridTerminalSystem.GetBlockWithName(InputData.DestinationContainerName).GetInventory();

                // Инициализируем парсер
                InputPanelTextParser parser = new InputPanelTextParser();

                // Инициализируем словарь типа "подтип-количество"
                List<TransferItem> transferItems = new List<TransferItem>();

                // Если парсер распарсил данные панели ввода в словарь
                if (parser.parseInputPanelText(PanelWriter.InputPanel, transferItems)) {

                    // get actual item snapshots from supply storage
                    List<MyInventoryItem> items = new List<MyInventoryItem>();
                    foreach (IMyInventory inventory in supplyStorage) {
                        List<MyInventoryItem> tempItems = new List<MyInventoryItem>();
                        inventory.GetItems(tempItems);
                        foreach (MyInventoryItem item in tempItems) {
                            items.Add(item);
                        }
                    }

                    // Инициализируем переносчик
                    SmartItemTransferer smartTransferer = new SmartItemTransferer();

                    // Инициализируем менеджер сборщика
                    AssemblerManager asmManager = new AssemblerManager(Worker.GridTerminalSystem.GetBlockWithName(InputData.AssemblerName) as IMyAssembler);

                    // do transfer
                    Worker.doTransfer(smartTransferer, supplyStorage, destinationInventory, transferItems, asmManager);
                }
            }

            // Метод возобновления работы
            public static void workResumption() {

                // get supply storage
                List<IMyInventory> supplyStorage = Worker.getSupplyStorage(InputData.SupplyContainerNames);

                // if supply storage is null then abort work
                if (supplyStorage == null) {
                    PanelWriter.writeOutputDataLine("Перенос предметов прерван. Причина: Ошибка данных хранилища припасов", true);
                    Worker.actualWorkState = WorkStates.Aborted;
                }

                // Берем инвентарь назначения
                IMyInventory destinationInventory = Worker.GridTerminalSystem.GetBlockWithName(InputData.DestinationContainerName).GetInventory();

                // Устанавливаем флаг, что работа снова в процесса
                Worker.actualWorkState = Worker.WorkStates.Processing;

                // Вынимаем из снимка умный переносчик предметов
                SmartItemTransferer smartTransferer = SmartItemTransfererSnapshot.Transferer;              

                // get actual item snapshots from supply storage
                List<MyInventoryItem> items = new List<MyInventoryItem>();
                foreach (IMyInventory inventory in supplyStorage) {
                    List<MyInventoryItem> tempItems = new List<MyInventoryItem>();
                    inventory.GetItems(tempItems);
                    foreach (MyInventoryItem item in tempItems) {
                        items.Add(item);
                    }
                }

                // do transfer
                Worker.doTransfer(smartTransferer, supplyStorage, destinationInventory, SmartItemTransfererSnapshot.Snapshot.TransferItems, SmartItemTransfererSnapshot.Snapshot.AsmManager);
            }


            // Набор состояний
            public enum WorkStates {
                // В ожидании начала работы
                WaitingStart,

                // В процессе работы
                Processing,

                // В ожидании ресурсов
                WaitingResources,

                // Работа завершена
                Completed,

                // Работа некорректно прервана
                Aborted
            }

        }


        // Класс, содержащий utils данные и методы для панели ввода
        static class InputPanelTextHelper {

            // Список подтипов компонентов
            public static string[] ComponentSubtypes { get; private set; } = new string[21]
            {
            "BulletproofGlass", "Computer", "Construction", "Detector", "Display", "Explosives",
            "Girder", "GravityGenerator", "InteriorPlate", "LargeTube", "Medical", "MetalGrid", "Motor",
            "PowerCell", "RadioCommunication", "Reactor", "SmallTube", "SolarCell", "SteelPlate", "Superconductor",
            "Thrust"
            };

            // Список имён компонентов на русском языке
            public static string[] ComponentNamesRU { get; private set; } = new string[21]
            {
            "Бронированноестекло", "Компьютер", "Строительныекомпоненты", "Компонентыдетектора", "Экран", "Взрывчатка",
            "Балка", "Компонентыгравитационногогенератора", "Внутренняяпластина", "Большаястальнаятруба", "Медицинскиекомпоненты", "Компонентрешётки", "Мотор",
            "Энергоячейка", "Радиокомпоненты", "Компонентыреактора", "Малаятрубка", "Солнечнаяячейка", "Стальнаяпластина", "Сверхпроводник",
            "Деталиионногоускорителя"
            };

            // Словарь имя компонента на русском - подтип компонента
            public static Dictionary<string, string> ComponentNamesRUSubtypesENG { get; private set; } = new Dictionary<string, string>()
            {
            { "Бронированноестекло", "BulletproofGlass" }, { "Компьютер",  "Computer" }, { "Строительныекомпоненты", "Construction" },
            { "Компонентыдетектора", "Detector" }, { "Экран", "Display" }, { "Взрывчатка", "Explosives" },
            { "Балка", "Girder" }, { "Компонентыгравитационногогенератора", "GravityGenerator" }, { "Внутренняяпластина", "InteriorPlate" },
            { "Большаястальнаятруба", "LargeTube" }, { "Медицинскиекомпоненты", "Medical" }, { "Компонентрешётки", "MetalGrid" },
            { "Мотор", "Motor" }, { "Энергоячейка", "PowerCell" }, { "Радиокомпоненты", "RadioCommunication" },
            { "Компонентыреактора", "Reactor" }, { "Малаятрубка", "SmallTube" }, { "Солнечнаяячейка", "SolarCell" },
            { "Стальнаяпластина", "SteelPlate" }, { "Сверхпроводник", "Superconductor" }, { "Деталиионногоускорителя", "Thrust" }
            };

            // Заглавие текста по умолчанию
            public static string DefaultTextTitle { get; private set; } = "Список запрашиваемых компонентов: ";

            // Метод установки стандартного вида дисплея
            public static void setDefaultSurfaceView(IMyTextPanel panel) {
                panel.BackgroundColor = Color.Black;
                panel.FontColor = Color.Yellow;
                panel.FontSize = 0.7f;
            }

            // Метод записи стандартного текста в панель ввода
            public static void writeDefaultText(IMyTextPanel inputPanel) {
                inputPanel.WriteText(DefaultTextTitle + '\n', false);

                foreach (string componentNameRU in ComponentNamesRU) {
                    inputPanel.WriteText(componentNameRU + " = 0" + '\n', true);
                }
            }

            // Метод проверки строки на соответствие заглавию текста по умолчанию
            public static bool isDefaultText(string text) {
                return text == DefaultTextTitle;
            }
        }

        // Класс-парсер данных компонентов из панели ввода
        class InputPanelTextParser {
            // Необходимый размер данных
            public const int requiredDataStringsSize = 21;

            // Метод список предметов на перенос
            public bool parseInputPanelText(IMyTextPanel inputPanel, List<TransferItem> transferItems) {
                // Очищаем заполняемый словарь
                transferItems.Clear();

                // Инициализируем и заполняем динамическую строку текстом из панели ввода
                StringBuilder tempBuilder = new StringBuilder();
                inputPanel.ReadText(tempBuilder);

                // Проверка на пустоту и содержание символа перехода на следующую строку
                if (tempBuilder.ToString() == "" || !tempBuilder.ToString().Contains('\n')) {
                    Worker.actualWorkState = Worker.WorkStates.Aborted;
                    return false;
                }

                // Разбиваем динамическую строку на список неизменяемых строк
                List<string> inputPanelDataStrings = tempBuilder.ToString().Split('\n').ToList<string>();

                // Если первая строка в списке - не заглавие
                if (!InputPanelTextHelper.isDefaultText(inputPanelDataStrings[0])) {
                    Worker.actualWorkState = Worker.WorkStates.Aborted;
                    return false;
                }

                // Удаляем последний лишний перенос строки
                inputPanelDataStrings.Remove(inputPanelDataStrings[inputPanelDataStrings.Count - 1]);

                // Удаляем заглавие
                inputPanelDataStrings.Remove(inputPanelDataStrings.First());

                // Если размер сформированного списка не равен заданному
                if (inputPanelDataStrings.Count != requiredDataStringsSize) {
                    Worker.actualWorkState = Worker.WorkStates.Aborted;
                    return false;
                }

                // Проходим по каждой строке компонентов
                foreach (string componentString in inputPanelDataStrings) {

                    // Если строка данных компонента не содержит пробел или символ '='
                    if (!componentString.Contains(' ') || !componentString.Contains('=')) {
                        // Очищаем словарь т.к. в него уже могли добавится данные, без очистки словаря при обрыве его заполнения в нём останется мусор
                        transferItems.Clear();

                        PanelWriter.writeOutputDataLine("Парсер: зашли в не соедржит ' ' или = ", true);
                        PanelWriter.writeOutputDataLine(componentString, true);

                        Worker.actualWorkState = Worker.WorkStates.Aborted;

                        return false;
                    }


                    // Очищаем строку от пробелов
                    string newComponentString = componentString.Replace(" ", "");

                    // Разбиваем данные компонента по символу '='
                    string[] componentNameAmount = newComponentString.Split('=');



                    // Проверка на число
                    foreach (char ch in componentNameAmount[1]) {
                        // Если в строке, где должно быть количество запрошенных ресурсов, присутствует не цифра
                        if (!Char.IsDigit(ch)) {

                            // Откидываем лог для оператора
                            PanelWriter.writeOutputDataLine("Ошибка! В количество компонента передано не число", true);

                            // Переводим флаг в прерывание
                            Worker.actualWorkState = Worker.WorkStates.Aborted;

                            return false;
                        }
                    }

                    int amount = Int16.Parse(componentNameAmount[1]);

                    // Добавляем в словарь подтип компонента и количество
                    if (InputPanelTextHelper.ComponentNamesRUSubtypesENG.Keys.Contains(componentNameAmount[0]) && amount > 0) {
                        TransferItem transferItem = new TransferItem();

                        transferItem.setData(InputPanelTextHelper.ComponentNamesRUSubtypesENG[componentNameAmount[0]], amount, false);

                        PanelWriter.writeOutputDataLine("Мы добавили предмет " + transferItem.SubtypeId + " в transferItems ", true);

                        transferItems.Add(transferItem);

                        PanelWriter.writeOutputDataLine("Вывод traferItems", true);
                        foreach (TransferItem item in transferItems) {
                            PanelWriter.writeOutputDataLine(item.SubtypeId, true);
                        }

                        PanelWriter.writeOutputDataLine("Предмет содержится в списке?" + transferItems.Contains(transferItem).ToString(), true);
                    }

                }

                if (transferItems.Count < 1) {
                    PanelWriter.writeOutputDataLine("Перенос прерван, ни один предмет не был запрошен", true);
                    Worker.actualWorkState = Worker.WorkStates.Aborted;
                    return false;
                }

                return true;
            }
        }

        class AssemblerManager {
            // Сборщик
            public IMyAssembler Assembler { get; set; }

            public string BlueprintType { get; private set; } = "MyObjectBuilder_BlueprintDefinition/";

            public Dictionary<string, string> ComponentAndBlueprintSubtypes { get; private set; } = new Dictionary<string, string>()
            {
            { "BulletproofGlass", "BulletproofGlass" }, { "Computer",  "ComputerComponent" }, { "Construction", "ConstructionComponent" },
            { "Detector", "DetectorComponent" }, { "Display", "Display" }, { "Explosives", "ExplosivesComponent" },
            { "Girder", "GirderComponent" }, { "GravityGenerator", "GravityGeneratorComponent" }, { "InteriorPlate", "InteriorPlate" },
            { "LargeTube", "LargeTube" }, { "Medical", "MedicalComponent" }, { "MetalGrid", "MetalGrid" },
            { "Motor", "MotorComponent" }, { "PowerCell", "PowerCell" }, { "RadioCommunication", "RadioCommunicationComponent" },
            { "Reactor", "ReactorComponent" }, { "SmallTube", "SmallTube" }, { "SolarCell", "SolarCell" },
            { "SteelPlate", "SteelPlate" }, { "Superconductor", "Superconductor" }, { "Thrust", "ThrustComponent" }
            };

            // Конструктор по умолчанию
            public AssemblerManager(IMyAssembler assembler) {
                Assembler = assembler;
            }

            public bool assembleComponent(string subtypeId, long amount) {

                if (!ComponentAndBlueprintSubtypes.Keys.Contains(subtypeId)) {
                    PanelWriter.writeOutputDataLine("Компонент с подтипом: " + subtypeId, true);
                    PanelWriter.writeOutputDataLine("не содержится в словаре ComponentAndBlueprintSubtypes.", true);

                    return false;
                }

                // Получаем MyDefinitionId из item
                MyDefinitionId defID = MyDefinitionId.Parse(this.BlueprintType + this.ComponentAndBlueprintSubtypes[subtypeId]);

                // Получаем MyFixedPoint из long
                MyFixedPoint fpAmount = new MyFixedPoint();
                fpAmount.RawValue = amount;

                PanelWriter.writeOutputDataLine("defID: " + defID, true);

                PanelWriter.writeOutputDataLine((this.Assembler.CanUseBlueprint(defID)).ToString(), true);

                // Добавляем в очередь создание предмета item в количестве amount
                this.Assembler.AddQueueItem(defID, fpAmount);

                PanelWriter.writeOutputDataLine("ПОСЛЕ адд ту квае", true);


                return true;
            }

        }

        // Глобальный класс снимка умного переносчика ресурсов
        static class SmartItemTransfererSnapshot {

            // Переносчик предметов
            public static SmartItemTransferer Transferer { get; private set; }

            // Снимок метода smartTransferTo
            public static SmartTransferToSnapshot Snapshot { get; private set; }

            public static void saveSnapshot(SmartItemTransferer transferer, SmartTransferToSnapshot snapshot) {
                SmartItemTransfererSnapshot.Transferer = transferer;
                SmartItemTransfererSnapshot.Snapshot = snapshot;
            }

            // Класс-снимок метода smartTransferTo класса ItemTransferer
            public class SmartTransferToSnapshot {

                // Отправной инвентарь
                public List<IMyInventory> SupplyStorage { get; private set; }

                // Инвентарь назначения
                public IMyInventory DestinationInventory { get; private set; }

                // Список предметов на перенос
                public List<TransferItem> TransferItems { get; private set; }

                // Менеджер сборщика
                public AssemblerManager AsmManager { get; private set; }

                // Конструктор по умолчанию
                public SmartTransferToSnapshot(List<IMyInventory> supplyStorage, IMyInventory destinationInventory, List<TransferItem> transferItems, AssemblerManager asmManager) {
                    this.SupplyStorage = supplyStorage;
                    this.DestinationInventory = destinationInventory;
                    this.TransferItems = transferItems;
                    this.AsmManager = asmManager;
                }
            }
        }

        // Глобальный класс для записи в дисплеи
        static class PanelWriter {

            public static IMyTextPanel InputPanel { get; set; }

            public static IMyTextPanel OutputPanel { get; set; }

            public static void writeInputData(string text, bool append = false) {
                InputPanel.WriteText(text, append);
            }

            public static void writeInputData(StringBuilder text, bool append = false) {
                InputPanel.WriteText(text, append);
            }

            public static void writeOutputData(string text, bool append = false) {
                OutputPanel.WriteText(text, append);
            }

            public static void writeOutputData(StringBuilder text, bool append = false) {
                OutputPanel.WriteText(text, append);
            }

            public static void writeOutputDataLine(string text, bool append = false) {
                OutputPanel.WriteText(text + '\n', append);
            }

            public static void writeOutputDataLine(StringBuilder text, bool append = false) {
                OutputPanel.WriteText(text.Append('\n'), append);
            }

        }



        public Program() {

            // Берем панель ввода
            IMyTextPanel inputPanel = GridTerminalSystem.GetBlockWithName(InputData.InputPanelName) as IMyTextPanel;
            // Берем панель вывода
            IMyTextPanel outputPanel = GridTerminalSystem.GetBlockWithName(InputData.OutputPanelName) as IMyTextPanel;

            // 
            PanelWriter.InputPanel = inputPanel;
            PanelWriter.OutputPanel = outputPanel;

            PanelWriter.writeOutputData("");

            // Устанавливаем панели вывода стандартный вид
            InputPanelTextHelper.setDefaultSurfaceView(outputPanel);

            // Устанавливаем панели ввода стандартный вид и вводим исходные данные
            InputPanelTextHelper.setDefaultSurfaceView(inputPanel);
            InputPanelTextHelper.writeDefaultText(inputPanel);

            //
            Worker.GridTerminalSystem = GridTerminalSystem;

            // Устанавливаем частоту тиков скрипта на раз в ~1.5 секунды
            Runtime.UpdateFrequency = UpdateFrequency.Update100;
        }

        public void Main(string argument, UpdateType updateSource) {

            switch (Worker.actualWorkState) {

                case Worker.WorkStates.WaitingStart:

                    // Если передали аргумент "start", то начинаем работу
                    if (argument == "start") {

                        PanelWriter.writeOutputDataLine("Мы начали работу", false);

                        Worker.work();
                    }

                    break;

                case Worker.WorkStates.Processing:


                    PanelWriter.writeOutputDataLine("Мы в работе", true);

                    // Ничего не делаем

                    break;

                case Worker.WorkStates.WaitingResources:

                    PanelWriter.writeOutputDataLine("Мы ожидаем ресурсы", true);

                    // Пытаемся восстановить работу
                    Worker.workResumption();

                    break;

                case Worker.WorkStates.Completed:

                    // Тута выводим лог о завершении работы
                    PanelWriter.writeOutputDataLine("Перенос предметов завершен!", true);


                    // Устанавливаем стандартные значения после таймаута
                    // TODO: вынести это и это же из Program() в класс
                    IMyTextPanel inputPanel = GridTerminalSystem.GetBlockWithName(InputData.InputPanelName) as IMyTextPanel;
                    IMyTextPanel outputPanel = GridTerminalSystem.GetBlockWithName(InputData.OutputPanelName) as IMyTextPanel;


                    InputPanelTextHelper.setDefaultSurfaceView(outputPanel);
                    InputPanelTextHelper.setDefaultSurfaceView(inputPanel);
                    InputPanelTextHelper.writeDefaultText(inputPanel);

                    Worker.resetWorkState();

                    break;

                case Worker.WorkStates.Aborted:

                    // Тута выводим лог о завершении работы
                    PanelWriter.writeOutputDataLine("Перенос прерван!", true);


                    // Устанавливаем стандартные значения после таймаута
                    // TODO: вынести это и это же из Program() в класс
                    IMyTextPanel inputP = GridTerminalSystem.GetBlockWithName(InputData.InputPanelName) as IMyTextPanel;
                    IMyTextPanel outputP = GridTerminalSystem.GetBlockWithName(InputData.OutputPanelName) as IMyTextPanel;


                    InputPanelTextHelper.setDefaultSurfaceView(outputP);
                    InputPanelTextHelper.setDefaultSurfaceView(inputP);
                    InputPanelTextHelper.writeDefaultText(inputP);

                    Worker.resetWorkState();

                    break;

                default:
                    break;

            }
        }
        public void Save() {

        }
        #endregion
    }
}
