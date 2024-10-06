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
        // TODO: check for available volume supply and destination containers
        // TODO: dictionary subtypeId - component volume
        // TODO: replace using inventories by blocks for advanced logs
        // TODO: request for assemble all missing items immediately
        // TODO: add argument for abort transfer

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

            // destination container names
            static public List<string> DestinationContainerNames { get; private set; } = new List<string> {
                "SMT Destination Container 1", "SMT Destination Container 2"
            };

            // assembler names
            static public List<string> AssemblerNames { get; private set; } = new List<string> {
                "SMT Assembler 1", "SMT Assembler 2"
             };
        }

        class TransferItem {
            public string SubtypeId { get; private set; }

            public MyFixedPoint TransferRequestedAmount { get; set; }

            public bool IsAssembleRequested { get; set; }

            public TransferItem() {
                SubtypeId = "";
                TransferRequestedAmount = -1;

                IsAssembleRequested = false;
            }

            public void setData(string subtypeId, MyFixedPoint amount, bool isAssembleRequested) {

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

            NotEnoughtSupplyStorageVolume,

            NotEnoughDestinationStorageVolume,

            NotEnoughtComponents

        }

        // Класс перемещения предметов из одного инвентаря в другой инвентарь
        class SmartItemTransferer {

            // Метод перемещения предметов с заказом крафта, если предметов не достаточно
            public SmartTransferResult smartTransferTo(List<IMyInventory> supplyStorage, List<IMyInventory> destinationStorage, List<TransferItem> transferItems, List<IMyAssembler> assemblers) {

                // if input data is invalid then return input data error
                if (supplyStorage == null || supplyStorage.Count <= 0
                    || destinationStorage == null || destinationStorage.Count <= 0
                    || transferItems == null || transferItems.Count <= 0
                    || assemblers == null || assemblers.Count <= 0) return SmartTransferResult.InputDataError;


                MyFixedPoint itemVolume = new MyFixedPoint();        
                foreach (TransferItem transferItem in transferItems) {
                    itemVolume += VolumeCalculator.itemVolumeM3(transferItem.SubtypeId, transferItem.TransferRequestedAmount);
                }

                if (!VolumeCalculator.isEnoughVolume(destinationStorage, itemVolume)) return SmartTransferResult.NotEnoughDestinationStorageVolume;


                foreach (TransferItem transferItem in transferItems) {

                    // if item isn't be transfered
                    if (transferItem.TransferRequestedAmount <= 0) continue;

                    // get item amount in storage
                    MyFixedPoint storageItemAmount = availableItemAmount(supplyStorage, transferItem.SubtypeId);


                    // if not enought items in storage for make transfer
                    if (storageItemAmount < transferItem.TransferRequestedAmount) {

                        // get item amount for assemble
                        MyFixedPoint transferItemAssembleAmount = transferItem.TransferRequestedAmount;

                        // if storage item amount is correct then reduce item amount for assemble by storage item amount
                        if (storageItemAmount > 0) transferItemAssembleAmount -= storageItemAmount;


                        // if item isn't requested for assemble
                        if (!transferItem.IsAssembleRequested) {

                            // on assemble request flag for item
                            transferItem.IsAssembleRequested = true;

                            // get item amount that already crafting
                            MyFixedPoint queueItemAmount = AssemblerManager.itemCraftAmount(assemblers, transferItem.SubtypeId);

                            // if already craft amount > 0 and less then item amount for request assemble then reduce item amount for request assemble
                            if (queueItemAmount > 0 && queueItemAmount < transferItemAssembleAmount) transferItemAssembleAmount -= queueItemAmount;

                            // else if already craft amount >= item amount for assemble then reset last one
                            else if (queueItemAmount >= transferItemAssembleAmount) transferItemAssembleAmount = 0;

                            // if item amount for assemble isn't reset
                            if (transferItemAssembleAmount > 0) {

                                // if assemble isn't made then return assemble error
                                if (!AssemblerManager.assembleComponent(assemblers, transferItem.SubtypeId, transferItemAssembleAmount)) return SmartTransferResult.AssemblerError;
                            }

                        }

                        // save snapshot
                        SmartItemTransfererSnapshot.saveSnapshot(this, new SmartItemTransfererSnapshot.SmartTransferToSnapshot(supplyStorage, destinationStorage, transferItems, assemblers));

                        return SmartTransferResult.NotEnoughtComponents;
                    }

                    SmartTransferResult transferResult = multiTransferTo(supplyStorage, destinationStorage, transferItem);

                    if (transferResult != SmartTransferResult.Succesful) return transferResult;

                }

                return SmartTransferResult.Succesful;
            }

            static public MyFixedPoint availableItemAmount(List<IMyInventory> inventories, string itemSubtypeID) {

                MyFixedPoint availableItemAmount = 0;

                foreach (IMyInventory inventory in inventories) {

                    List<MyInventoryItem> inventoryItems = new List<MyInventoryItem>();
                    inventory.GetItems(inventoryItems);
                    foreach (MyInventoryItem item in inventoryItems) {

                        if (item.Type.SubtypeId == itemSubtypeID) availableItemAmount += item.Amount;

                    }

                }

                return availableItemAmount;
            }

            static public SmartTransferResult multiTransferTo(List<IMyInventory> supplyStorage, List<IMyInventory> destinationStorage, TransferItem transferItem) {

                // if supply/destination storage is empty or requested transfer amount is 0
                if (supplyStorage == null || supplyStorage.Count <= 0
                    || destinationStorage == null || destinationStorage.Count <= 0 
                    || transferItem.TransferRequestedAmount <= 0) return SmartTransferResult.InputDataError;

                // if dictionary not contains transfer item subTypeId
                if(!VolumeCalculator.SubTypeIdVolume.ContainsKey(transferItem.SubtypeId)) return SmartTransferResult.InputDataError;


                foreach (IMyInventory supplyInventory in supplyStorage) {
                    
                    // get inventory items
                    List<MyInventoryItem> supplyItems = new List<MyInventoryItem>();
                    supplyInventory.GetItems(supplyItems);

                    foreach (MyInventoryItem supplyItem in supplyItems) {

                        // if subtype isn't equals then skip that item
                        if (supplyItem.Type.SubtypeId != transferItem.SubtypeId) continue;

                        // get item amount in current inventory
                        MyFixedPoint availableItemAmount = supplyItem.Amount;


                        foreach (IMyInventory destinationInventory in destinationStorage) {

                            // if destination inventory is full then skip it
                            if (destinationInventory.CurrentVolume + VolumeCalculator.itemVolumeM3(transferItem.SubtypeId, 1) > destinationInventory.MaxVolume) continue;


                            // init current transfer amount
                            MyFixedPoint currentInventoryTransferAmount = 0;

                            // if inventory has enought item for transfer then set current amount transfer from requested transfer amount
                            if (availableItemAmount >= transferItem.TransferRequestedAmount) currentInventoryTransferAmount = transferItem.TransferRequestedAmount;

                            // if inventory hasn't enought item for transfer then set current amount transfer equals item amount in current inventory
                            else currentInventoryTransferAmount = availableItemAmount;

                            // get summary volume of items number that we wanna to transfer
                            MyFixedPoint currentInventoryTransferVolume = VolumeCalculator.itemVolumeM3(transferItem.SubtypeId, currentInventoryTransferAmount);


                            // if we can't transfer item by current transfer amount
                            if (destinationInventory.CurrentVolume + currentInventoryTransferVolume > destinationInventory.MaxVolume) {

                                // calculate current transfer amount like: "(max container volume - current container volume) / volume of one item"
                                currentInventoryTransferAmount = (int)(((float)(destinationInventory.MaxVolume - destinationInventory.CurrentVolume)) / (float)VolumeCalculator.itemVolumeM3(transferItem.SubtypeId, 1));

                                // if current transfer amount > remaining requested amount then set current transfer amount from remaining requested amount
                                if (currentInventoryTransferAmount > transferItem.TransferRequestedAmount) currentInventoryTransferAmount = transferItem.TransferRequestedAmount;

                                // do transfer
                                if (!supplyInventory.TransferItemTo(destinationInventory, supplyItem, currentInventoryTransferAmount)) return SmartTransferResult.TransferError;

                                // reduce transfer requested amount by transfered amount
                                transferItem.TransferRequestedAmount -= currentInventoryTransferAmount;

                                // reduce available item amount in current supply inventory by transferred amount
                                availableItemAmount -= currentInventoryTransferAmount;

                            } else {

                                // if current transfer amount > remaining requested amount then set current transfer amount from remaining requested amount
                                if (currentInventoryTransferAmount > transferItem.TransferRequestedAmount) currentInventoryTransferAmount = transferItem.TransferRequestedAmount;

                                // do transfer
                                if (!supplyInventory.TransferItemTo(destinationInventory, supplyItem, currentInventoryTransferAmount)) return SmartTransferResult.TransferError;

                                // reduce transfer requested amount by transfered amount
                                transferItem.TransferRequestedAmount -= currentInventoryTransferAmount;

                                // reduce available item amount in current supply inventory by transferred amount
                                availableItemAmount -= currentInventoryTransferAmount;

                                break;
                            }

                        }
                       
                    }

                    // if requested transfer amount <= 0 means that we already transferred items
                    if (transferItem.TransferRequestedAmount <= 0) break;

                }

                // check for maybe we couldn't transfer items 
                if (transferItem.TransferRequestedAmount > 0) return SmartTransferResult.TransferError;


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

            private static List<IMyInventory> getStorage(List<string> containerNames) {

                // init inventory storage
                List<IMyInventory> storage = new List<IMyInventory>();

                foreach (string containerName in containerNames) {

                    // get block
                    IMyCargoContainer block = GridTerminalSystem.GetBlockWithName(containerName) as IMyCargoContainer;

                    // if block is null then we can't find it and return null 
                    if (block == null) return null;

                    // add block inventory to storage
                    storage.Add(block.GetInventory());
                }

                return storage;
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

            private static void doTransfer(SmartItemTransferer smartTransferer, List<IMyInventory> supplyStorage, List<IMyInventory> destinationStorage, List<TransferItem> transferItems, List<IMyAssembler> assemblers) {

                switch (smartTransferer.smartTransferTo(supplyStorage, destinationStorage, transferItems, assemblers)) {

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

                    case SmartTransferResult.NotEnoughtSupplyStorageVolume: {
                            PanelWriter.writeOutputDataLine("Перенос предметов прерван. Причина: В исходном хранилище предметов \n не хватает места для приема собранных предметов", true);
                            Worker.actualWorkState = Worker.WorkStates.Aborted;
                            break;
                        }

                    case SmartTransferResult.NotEnoughDestinationStorageVolume: {
                            PanelWriter.writeOutputDataLine("Перенос предметов прерван. Причина: В хранилище назначения не хватает места для приема предметов", true);
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
                List<IMyInventory> supplyStorage = Worker.getStorage(InputData.SupplyContainerNames);

                // if supply storage is null then abort work
                if (supplyStorage == null) {
                    PanelWriter.writeOutputDataLine("Перенос предметов прерван. Причина: Ошибка данных хранилища припасов", true);
                    Worker.actualWorkState = WorkStates.Aborted;
                    return;
                }

                // get destination storage
                List<IMyInventory> destinationStorage = Worker.getStorage(InputData.DestinationContainerNames);

                if(destinationStorage == null) {
                    PanelWriter.writeOutputDataLine("Перенос предметов прерван. Причина: Ошибка данных хранилища назначения", true);
                    Worker.actualWorkState = WorkStates.Aborted;
                    return;
                }

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
                    List<IMyAssembler> assemblers = Worker.getAssemblers(InputData.AssemblerNames);

                    if (assemblers == null) {
                        PanelWriter.writeOutputDataLine("Перенос предметов прерван. Причина: Сборщики не найден", true);
                        Worker.actualWorkState = WorkStates.Aborted;
                        return;
                    }

                    // do transfer
                    Worker.doTransfer(smartTransferer, supplyStorage, destinationStorage, transferItems, assemblers);
                }
            }

            // Метод возобновления работы
            public static void workResumption() {

                // get supply storage
                List<IMyInventory> supplyStorage = Worker.getStorage(InputData.SupplyContainerNames);

                // if supply storage is null then abort work
                if (supplyStorage == null) {
                    PanelWriter.writeOutputDataLine("Перенос предметов прерван. Причина: Ошибка данных хранилища припасов", true);
                    Worker.actualWorkState = WorkStates.Aborted;
                    return;
                }

                // get destination storage
                List<IMyInventory> destinationStorage = Worker.getStorage(InputData.DestinationContainerNames);

                if (destinationStorage == null) {
                    PanelWriter.writeOutputDataLine("Перенос предметов прерван. Причина: Ошибка данных хранилища назначения", true);
                    Worker.actualWorkState = WorkStates.Aborted;
                    return;
                }

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
                Worker.doTransfer(smartTransferer, supplyStorage, destinationStorage, SmartItemTransfererSnapshot.Snapshot.TransferItems, SmartItemTransfererSnapshot.Snapshot.Assemblers);
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

        static class VolumeCalculator {

            static public Dictionary<string, float> SubTypeIdVolume = new Dictionary<string, float>() {
                { "BulletproofGlass", 8f }, { "Computer", 1f }, { "Construction", 2f },
                { "Detector", 6f }, { "Display", 6f }, { "Explosives", 2f },
                { "Girder", 2f }, { "GravityGenerator", 200f }, { "InteriorPlate",  5f},
                { "LargeTube", 38f }, { "Medical", 160f }, { "MetalGrid", 15f },
                { "Motor", 8f }, { "PowerCell", 45f }, { "RadioCommunication", 70f },
                { "Reactor", 8f }, { "SmallTube", 2f }, { "SolarCell", 20f },
                { "SteelPlate", 3f }, { "Superconductor", 8f }, { "Thrust", 10f }
            };

            static public float m3InOneLiter = 0.001f;

            static public bool isEnoughVolume(List<IMyInventory> inventories, MyFixedPoint itemVolume) {

                MyFixedPoint availableStorageVolume = 0;
                
                foreach (IMyInventory inventory in inventories) {
                    availableStorageVolume += inventory.MaxVolume - inventory.CurrentVolume;
                }

                return availableStorageVolume >= itemVolume;
            }

            static public bool isEnoughVolume(IMyInventory inventory, MyFixedPoint itemVolume) {
                return (inventory.MaxVolume - inventory.CurrentVolume) >= itemVolume;
            }

            static public MyFixedPoint itemVolumeM3(string subTypeId, MyFixedPoint itemCount) {
                if (!SubTypeIdVolume.ContainsKey(subTypeId)) return 0; // TODO: exception

                float itemVolume = 0;
                SubTypeIdVolume.TryGetValue(subTypeId, out itemVolume);

                return itemVolume * itemCount * m3InOneLiter;
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
                    PanelWriter.writeOutputDataLine("Parser error. Invalid default text");
                    Worker.actualWorkState = Worker.WorkStates.Aborted;
                    return false;
                }

                // Разбиваем динамическую строку на список неизменяемых строк
                List<string> inputPanelDataStrings = tempBuilder.ToString().Split('\n').ToList<string>();

                // Если первая строка в списке - не заглавие
                if (!InputPanelTextHelper.isDefaultText(inputPanelDataStrings[0])) {
                    PanelWriter.writeOutputDataLine("Parser error. Title not found");
                    Worker.actualWorkState = Worker.WorkStates.Aborted;
                    return false;
                }

                // Удаляем последний лишний перенос строки
                inputPanelDataStrings.Remove(inputPanelDataStrings[inputPanelDataStrings.Count - 1]);

                // Удаляем заглавие
                inputPanelDataStrings.Remove(inputPanelDataStrings.First());

                // Если размер сформированного списка не равен заданному
                if (inputPanelDataStrings.Count != requiredDataStringsSize) {
                    PanelWriter.writeOutputDataLine("Parser error. String lenghts is not equal");
                    Worker.actualWorkState = Worker.WorkStates.Aborted;
                    return false;
                }

                // Проходим по каждой строке компонентов
                foreach (string componentString in inputPanelDataStrings) {

                    // Если строка данных компонента не содержит пробел или символ '='
                    if (!componentString.Contains(' ') || !componentString.Contains('=')) {
                        // Очищаем словарь т.к. в него уже могли добавится данные, без очистки словаря при обрыве его заполнения в нём останется мусор
                        transferItems.Clear();

                        PanelWriter.writeOutputDataLine("Парсер: зашли в не соедржит ' ' или = ");
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
                            PanelWriter.writeOutputDataLine("Ошибка! В количество компонента передано не число");

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

                        transferItems.Add(transferItem);
                    }

                }

                if (transferItems.Count < 1) {
                    PanelWriter.writeOutputDataLine("Parser error. No item requested for transfer");
                    Worker.actualWorkState = Worker.WorkStates.Aborted;
                    return false;
                }

                PanelWriter.writeOutputDataLine("Parse completed");

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


            static public bool assembleComponent(IMyAssembler assembler, string subtypeId, MyFixedPoint amount) {

                if (!ComponentAndBlueprintSubtypes.Keys.Contains(subtypeId)) {
                    PanelWriter.writeOutputDataLine("Компонент с подтипом: " + subtypeId, true);
                    PanelWriter.writeOutputDataLine("не содержится в словаре ComponentAndBlueprintSubtypes.", true);

                    return false;
                }

                // Получаем MyDefinitionId из item
                MyDefinitionId defID = MyDefinitionId.Parse(AssemblerManager.BlueprintType + AssemblerManager.ComponentAndBlueprintSubtypes[subtypeId]);

                PanelWriter.writeOutputDataLine("defID: " + defID, true);

                PanelWriter.writeOutputDataLine((assembler.CanUseBlueprint(defID)).ToString(), true);

                // Добавляем в очередь создание предмета item в количестве amount
                assembler.AddQueueItem(defID, amount);

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

            static public MyFixedPoint itemCraftAmount(List<IMyAssembler> assemblers, string itemSubtypeID) {

                MyFixedPoint targetItemCraftCount = 0;

                foreach (IMyAssembler assembler in assemblers) {

                    // get items in craft queue
                    List<MyProductionItem> queueItems = new List<MyProductionItem>();
                    assembler.GetQueue(queueItems);

                    // make MyDefinitionId from item subtypeId
                    MyDefinitionId targetItemDefinitionID = MyDefinitionId.Parse(AssemblerManager.BlueprintType + AssemblerManager.ComponentAndBlueprintSubtypes[itemSubtypeID]);

                    foreach (MyProductionItem queueItem in queueItems) {

                        // if got item has equals definition id with target item 
                        if (queueItem.BlueprintId == targetItemDefinitionID) targetItemCraftCount += queueItem.Amount;

                    }


                }

                return targetItemCraftCount;

            }

            static public bool assembleComponent(List<IMyAssembler> assemblers, string subtypeId, MyFixedPoint amount) {

                // TODO: checks for block availability


                // activate cooperative mode for assemblers
                IMyAssembler masterAssembler = AssemblerManager.activateCooperativeMode(assemblers);

                if (masterAssembler == null) return false;

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
                public List<IMyInventory> DestinationStorage { get; private set; }

                // Список предметов на перенос
                public List<TransferItem> TransferItems { get; private set; }

                // Сборщики
                public List<IMyAssembler> Assemblers { get; private set; }

                // Конструктор по умолчанию
                public SmartTransferToSnapshot(List<IMyInventory> supplyStorage, List<IMyInventory> destinationStorage, List<TransferItem> transferItems, List<IMyAssembler> assemblers) {
                    this.SupplyStorage = supplyStorage;
                    this.DestinationStorage = destinationStorage;
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
