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

        // TODO: check for existing display panels 
        // TODO: check for availability all input data blocks
        // TODO: timeout
        // TODO: check for assemblers queues and maybe repeat request items

        /** InputData - статический класс, хранящий входные значения скрипта. Здесь необходимо менять названия блоков
         * 
         */
        static class InputData {
            // Имя панели ввода
            static public string InputPanelName { get; private set; } = "SMT Input Panel 1";

            // Имя панели вывода
            static public string OutputPanelName { get; private set; } = "SMT Output Panel 1";

            // supply container names
            static public List<string> SupplyContainerNames { get; private set; } = new List<string> { 
                "SMT Supply Container 1", "SMT Supply Container 2"
            };

            // Имя конечного контейнера
            static public string DestinationContainerName { get; private set; } = "SMT Destination Container 1";

            // assembler names
            static public List<string> AssemblerNames { get; private set; } = new List<string> {
                "SMT Assembler 1", "SMT Assembler 2"
             };
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
            public SmartTransferResult smartTransferTo(List<IMyInventory> supplyStorage, IMyInventory destinationInventory, List<TransferItem> transferItems, List<IMyAssembler> assemblers) {

                // if input data is invalid than return input data error
                if (supplyStorage.Count <= 0 || destinationInventory == null || transferItems.Count <= 0 || assemblers == null) return SmartTransferResult.InputDataError;

                foreach (TransferItem transferItem in transferItems) {

                    // if item isn't be transfered
                    if (transferItem.TransferRequestedAmount <= 0) continue;


                    // if not enought items in storage for make transfer
                    if (calculateAvailableItemAmount(supplyStorage, transferItem.SubtypeId) < transferItem.TransferRequestedAmount) {

                        // if item isn't requested for assemble
                        if (!transferItem.IsAssembleRequested) {
                            // on assemble request flag for item
                            transferItem.IsAssembleRequested = true;

                            // get item amount that already crafting
                            int craftCount =  AssemblerManager.itemCraftCount(assemblers, transferItem.SubtypeId);

                            // if already craft amount > 0 than reduce item amount for request assemble
                            if (craftCount > 0) transferItem.TransferRequestedAmount -= craftCount;

                            // if requested amount is negative than reset requested amount
                            if(transferItem.TransferRequestedAmount < 0) transferItem.TransferRequestedAmount = 0;

                            // if assemble isn't made than return assemble error
                            if (!AssemblerManager.assembleComponent(assemblers, transferItem.SubtypeId, transferItem.TransferRequestedAmount)) return SmartTransferResult.AssemblerError;

                        }

                        // save snapshot
                        SmartItemTransfererSnapshot.saveSnapshot(this, new SmartItemTransfererSnapshot.SmartTransferToSnapshot(supplyStorage, destinationInventory, transferItems, assemblers));

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

                        if (item.Type.SubtypeId == itemSubtypeID) availableItemAmount += item.Amount.ToIntSafe();

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

                        // if item amount for transfer <= 0
                        if (transferItem.TransferRequestedAmount <= 0) continue;


                        // if items amount >= item amount for transfer
                        if (supplyItem.Amount.ToIntSafe() >= transferItem.TransferRequestedAmount) {

                            PanelWriter.writeOutputDataLine("[>=] Start transfer for item " + transferItem.SubtypeId + " .\n Item count in storage " + supplyItem.Amount.ToString(), true);

                            // init MyFixedPoint from item amount for transfer (long) 
                            MyFixedPoint fp = (int)transferItem.TransferRequestedAmount;

                            // if transfer isn't made than return transfer error
                            if (!supplyInventory.TransferItemTo(destinationInventory, supplyItem, fp)) return SmartTransferResult.TransferError;

                            // reset item amount for transfer
                            transferItem.TransferRequestedAmount = 0;

                            PanelWriter.writeOutputDataLine("[>=] Succesful transfer for item " + transferItem.SubtypeId + " . New transfer requested amount " + transferItem.TransferRequestedAmount.ToString(), true);
                        }

                        // if items amount < item amount for transfer
                        else if (supplyItem.Amount.ToIntSafe() < transferItem.TransferRequestedAmount) {

                            PanelWriter.writeOutputDataLine("[<] Start transfer for item " + transferItem.SubtypeId + " .\n Item count in storage " + supplyItem.Amount.ToString(), true);

                            // if transfer isn't made than return transfer error
                            if (!supplyInventory.TransferItemTo(destinationInventory, supplyItem)) return SmartTransferResult.TransferError;

                            // reduce item amount for transfer to transfered item amount
                            transferItem.TransferRequestedAmount -= supplyItem.Amount.ToIntSafe();

                            PanelWriter.writeOutputDataLine("[<] Succesful transfer for item " + transferItem.SubtypeId + " . New transfer requested amount " + transferItem.TransferRequestedAmount.ToString(), true);
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

            private static List<IMyAssembler> getAssemblers(List<string> assemblerNames) {

                List<IMyAssembler> assemblers = new List<IMyAssembler>();

                foreach (string assemblerName in assemblerNames) {

                    // get assembler
                    IMyAssembler assembler = GridTerminalSystem.GetBlockWithName(assemblerName) as IMyAssembler;

                    // if assembler is null then we can't find it and return null 
                    if (assembler == null) return null;

                    // add assembler in list
                    assemblers.Add(assembler);
                }

                return assemblers;
            }

            private static void doTransfer(SmartItemTransferer smartTransferer, List<IMyInventory> supplyStorage, IMyInventory destinationInventory, List<TransferItem> transferItems, List<IMyAssembler> assemblers) {

                switch (smartTransferer.smartTransferTo(supplyStorage, destinationInventory, transferItems, assemblers)) {

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
                    return;
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

                    // get assemblers
                    List<IMyAssembler> assemblers =  Worker.getAssemblers(InputData.AssemblerNames);

                    if (assemblers == null) {
                        PanelWriter.writeOutputDataLine("Перенос предметов прерван. Причина: Сборщики не найден", true);
                        Worker.actualWorkState = WorkStates.Aborted;
                        return;
                    }

                    // do transfer
                    Worker.doTransfer(smartTransferer, supplyStorage, destinationInventory, transferItems, assemblers);
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
                    return;
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
                Worker.doTransfer(smartTransferer, supplyStorage, destinationInventory, SmartItemTransfererSnapshot.Snapshot.TransferItems, SmartItemTransfererSnapshot.Snapshot.Assemblers);
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
                    PanelWriter.writeOutputDataLine("Parser error. Invalid default text", true);
                    Worker.actualWorkState = Worker.WorkStates.Aborted;
                    return false;
                }

                // Разбиваем динамическую строку на список неизменяемых строк
                List<string> inputPanelDataStrings = tempBuilder.ToString().Split('\n').ToList<string>();

                // Если первая строка в списке - не заглавие
                if (!InputPanelTextHelper.isDefaultText(inputPanelDataStrings[0])) {
                    PanelWriter.writeOutputDataLine("Parser error. Title not found", true);
                    Worker.actualWorkState = Worker.WorkStates.Aborted;
                    return false;
                }

                // Удаляем последний лишний перенос строки
                inputPanelDataStrings.Remove(inputPanelDataStrings[inputPanelDataStrings.Count - 1]);

                // Удаляем заглавие
                inputPanelDataStrings.Remove(inputPanelDataStrings.First());

                // Если размер сформированного списка не равен заданному
                if (inputPanelDataStrings.Count != requiredDataStringsSize) {
                    PanelWriter.writeOutputDataLine("Parser error. String lenghts is not equal", true);
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

        static class AssemblerManager {

            static public string BlueprintType { get; private set; } = "MyObjectBuilder_BlueprintDefinition/";

            static public Dictionary<string, string> ComponentAndBlueprintSubtypes { get; private set; } = new Dictionary<string, string>() {
                { "BulletproofGlass", "BulletproofGlass" }, { "Computer",  "ComputerComponent" }, { "Construction", "ConstructionComponent" },
                { "Detector", "DetectorComponent" }, { "Display", "Display" }, { "Explosives", "ExplosivesComponent" },
                { "Girder", "GirderComponent" }, { "GravityGenerator", "GravityGeneratorComponent" }, { "InteriorPlate", "InteriorPlate" },
                { "LargeTube", "LargeTube" }, { "Medical", "MedicalComponent" }, { "MetalGrid", "MetalGrid" },
                { "Motor", "MotorComponent" }, { "PowerCell", "PowerCell" }, { "RadioCommunication", "RadioCommunicationComponent" },
                { "Reactor", "ReactorComponent" }, { "SmallTube", "SmallTube" }, { "SolarCell", "SolarCell" },
                { "SteelPlate", "SteelPlate" }, { "Superconductor", "Superconductor" }, { "Thrust", "ThrustComponent" }
            };

            
            static public bool assembleComponent(IMyAssembler assembler, string subtypeId, long amount) {

                if (!ComponentAndBlueprintSubtypes.Keys.Contains(subtypeId)) {
                    PanelWriter.writeOutputDataLine("Компонент с подтипом: " + subtypeId, true);
                    PanelWriter.writeOutputDataLine("не содержится в словаре ComponentAndBlueprintSubtypes.", true);

                    return false;
                }

                // Получаем MyDefinitionId из item
                MyDefinitionId defID = MyDefinitionId.Parse(AssemblerManager.BlueprintType + AssemblerManager.ComponentAndBlueprintSubtypes[subtypeId]);

                // Получаем MyFixedPoint из long
                MyFixedPoint fpAmount = (int)amount;

                PanelWriter.writeOutputDataLine("defID: " + defID, true);

                PanelWriter.writeOutputDataLine((assembler.CanUseBlueprint(defID)).ToString(), true);

                // Добавляем в очередь создание предмета item в количестве amount
                assembler.AddQueueItem(defID, fpAmount);

                PanelWriter.writeOutputDataLine("ПОСЛЕ адд ту квае", true);


                return true;
            }

            static public IMyAssembler activateCooperativeMode(List<IMyAssembler> assemblers) {

                if (assemblers.Count < 1) return null;  // TODO: exception

                // deactivate cooperative mode for first assembler (it's master)
                assemblers.First().CooperativeMode = false;

                // activate cooperative mode for others
                for (int i = 1; i < assemblers.Count; i++) {
                    assemblers[i].CooperativeMode = true;
                }

                return assemblers.First();

            }

            static public int itemCraftCount(List<IMyAssembler> assemblers, string itemSubtypeID) {

                int targetItemCraftCount = 0;

                foreach (IMyAssembler assembler in assemblers) {

                    // get items in craft queue
                    List<MyProductionItem> queueItems = new List<MyProductionItem>();
                    assembler.GetQueue(queueItems);

                    // make MyDefinitionId from item subtypeId
                    MyDefinitionId targetItemDefinitionID = MyDefinitionId.Parse(AssemblerManager.BlueprintType + AssemblerManager.ComponentAndBlueprintSubtypes[itemSubtypeID]);

                    foreach (MyProductionItem queueItem in queueItems) {

                        // if got item has equals definition id with target item 
                        if (queueItem.BlueprintId == targetItemDefinitionID) targetItemCraftCount += queueItem.Amount.ToIntSafe();

                    }


                }

                return targetItemCraftCount;

            }

            static public bool assembleComponent(List<IMyAssembler> assemblers, string subtypeId, long amount) {

                // TODO: checks for block availability


                // activate cooperative mode for assemblers
                IMyAssembler masterAssembler =  AssemblerManager.activateCooperativeMode(assemblers);

                if(masterAssembler == null) return false;

                // assemble compoment on master assembler
                return AssemblerManager.assembleComponent(assemblers.First(), subtypeId, amount);
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

                // Сборщики
                public List<IMyAssembler> Assemblers { get; private set; }

                // Конструктор по умолчанию
                public SmartTransferToSnapshot(List<IMyInventory> supplyStorage, IMyInventory destinationInventory, List<TransferItem> transferItems, List<IMyAssembler> assemblers) {
                    this.SupplyStorage = supplyStorage;
                    this.DestinationInventory = destinationInventory;
                    this.TransferItems = transferItems;
                    this.Assemblers = assemblers;
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

                    // check for start argument
                    if (argument == "start") {

                        // start work
                        Worker.work();
                    }

                    break;

                case Worker.WorkStates.Processing:

                    // NO-OP

                    break;

                case Worker.WorkStates.WaitingResources:

                    // resump work
                    Worker.workResumption();

                    break;

                case Worker.WorkStates.Completed:

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
