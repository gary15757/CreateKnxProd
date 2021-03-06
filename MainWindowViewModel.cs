﻿using CreateKnxProd.Extensions;
using CreateKnxProd.Model;
using CreateKnxProd.Properties;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Xml;
using System.Xml.Serialization;

namespace CreateKnxProd
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        private RelayCommand _exportCommand;
        private RelayCommand _createNewCommand;
        private RelayCommand _openCommand;
        private RelayCommand _saveCommand;
        private RelayCommand _saveAsCommand;
        private RelayCommand _closeCommand;

        private IDialogService _dialogService;

        private const string _toolName = "KNX MT";
        private const string _toolVersion = "5.1.255.16695";

        private string _openFile = null;
        private KNX _model = null;

        private const string _fileExtension = ".xml";
        private readonly string _fileFilter = Ressources.XMLFiles;

        private ManufacturerData_tManufacturer _manufacturerData;
        private Hardware_t _hardware;
        private Hardware_tProduct _product;
        private CatalogSection_t _catalogSection;
        private CatalogSection_tCatalogItem _catalogItem;
        private ApplicationProgram_t _applicationProgram;
        private Hardware2Program_t _hardware2Program;
        private ApplicationProgramRef_t _appProgRef;
        private ApplicationProgramStatic_tCodeRelativeSegment _codeSegment;

        private ComObjectParameterBlock_t _parameterBlock;

        public MainWindowViewModel(IDialogService dialogService)
        {
            _dialogService = dialogService;
            _exportCommand = new RelayCommand(o => _model != null, Export);
            _saveCommand = new RelayCommand(o => _model != null, Save);
            _saveAsCommand = new RelayCommand(o => _model != null, SaveAs);
            _openCommand = new RelayCommand(o => true, Open);
            _createNewCommand = new RelayCommand(o => true, CreateNew);
            _closeCommand = new RelayCommand(o => _model != null, Close);
        }

        private void Open(object param)
        {
            try
            {
                if (_model != null)
                {
                    var cancel = AskSaveCancel();
                    if (cancel)
                        return;
                }

                var filePath = _dialogService.ChooseFileToOpen(_fileExtension, _fileFilter);
                if (filePath == null)
                    return;

                _openFile = filePath;

                XmlSerializer serializer = new XmlSerializer(typeof(KNX));
                using (var reader = new StreamReader(_openFile))
                {
                    _model = (KNX)serializer.Deserialize(reader);
                }

                _manufacturerData = _model.ManufacturerData.First();
                _hardware = _manufacturerData.Hardware.First();
                _product = _hardware.Products.First();
                _catalogSection = _manufacturerData.Catalog.First();
                _catalogItem = _catalogSection.CatalogItem.First();
                _hardware2Program = _hardware.Hardware2Programs.First();
                _applicationProgram = _manufacturerData.ApplicationPrograms.First();
                _appProgRef = _hardware2Program.ApplicationProgramRef.First();
                _codeSegment = _applicationProgram.Static.Code.RelativeSegment.First();

                var parameterList = _applicationProgram.Static.Parameters.OfType<Parameter_t>();

                foreach (var item in parameterList)
                {
                    item.AllTypes = ParameterTypes;
                    item.Type = ParameterTypes.First(t => t.Id == item.ParameterType);
                    Parameters.Add(item);
                }

                RaiseChanged();
            }
            catch (Exception ex)
            {
                ClearData();
                _dialogService.ShowMessage(ex.ToString());
            }
        }

        private void SaveAs(object param)
        {
            try
            {
                if (_model == null)
                    return;

                var filepath = _dialogService.ChooseSaveFile(_fileExtension, _fileFilter);
                if (filepath == null)
                    return;

                _openFile = filepath;
                Save(param);
            }
            catch (Exception ex)
            {
                _dialogService.ShowMessage(ex.ToString());
            }
        }

        private void Save(object param)
        {
            try
            {
                if (_model == null)
                    return;

                if (_openFile == null)
                    SaveAs(param);

                _product.RegistrationInfo = new RegistrationInfo_t() { RegistrationStatus = RegistrationStatus_t.Registered };
                _hardware2Program.RegistrationInfo = new RegistrationInfo_t()
                {
                    RegistrationStatus = RegistrationStatus_t.Registered,
                    RegistrationNumber = "0001/" + _hardware.VersionNumber.ToString() + _applicationProgram.ApplicationVersion
                };
                
                UpdateMediumInfo();
                SetEmptyListsNull();
                HandleParameters();
                RegenerateDynamic();
                CreateLoadProcedures();
                HandleComObjects();
                CorrectIds();



                XmlSerializer serializer = new XmlSerializer(typeof(KNX));
                using (var xmlWriter = new StreamWriter(_openFile, false, Encoding.UTF8))
                {
                    serializer.Serialize(xmlWriter, _model);
                }

                _dialogService.ShowMessage(Ressources.SaveSuccess);
            }
            catch (Exception ex)
            {
                _dialogService.ShowMessage(ex.ToString());
            }
        }

        private void UpdateMediumInfo()
        {
            if (_hardware2Program.MediumTypes[0] == "MT-5")
            {
                _applicationProgram.MaskVersion = "MV-57B0";
                _hardware.IsIPEnabled = true;
                _hardware.BusCurrent = 0;
                _hardware.BusCurrentSpecified = false;
            }
            else
            {
                _applicationProgram.MaskVersion = "MV-07B0";
                _hardware.IsIPEnabled = false;
                _hardware.BusCurrent = 10;
                _hardware.BusCurrentSpecified = true;
            }
        }

        private void HandleComObjects()
        {
            var appStatic = _applicationProgram.Static;
            appStatic.ComObjectRefs.Clear();

            uint i = 1;
            foreach (var item in appStatic.ComObjectTable.ComObject)
            {
                item.Number = i++;
                if (item.Text.Length <= 50)
                    item.Name = item.Text;
                else
                    item.Name = item.Text.Substring(0, 50);
                item.ReadOnInitFlag = Enable_t.Disabled;
                appStatic.ComObjectRefs.Add(new ComObjectRef_t() { ComObject = item, DatapointType = null, Roles = null });
                
            }
        }

        private void CreateLoadProcedures()
        {
            var ldProc1 = new LoadProcedures_tLoadProcedure();
            ldProc1.MergeId = 2;
            ldProc1.MergeIdSpecified = true;
            

            var ldCtrlCreate = new LoadProcedure_tLdCtrlRelSegment();
            ldCtrlCreate.LsmIdx = 4;
            ldCtrlCreate.LsmIdxSpecified = true;
            ldCtrlCreate.Mode = 0;
            ldCtrlCreate.Fill = 0;
            ldCtrlCreate.AppliesTo = LdCtrlProcType_t.full;
            ldCtrlCreate.Size = _codeSegment.Size;
            ldProc1.Items.Add(ldCtrlCreate);

            var ldProc2 = new LoadProcedures_tLoadProcedure();
            ldProc2.MergeId = 4;
            ldProc2.MergeIdSpecified = true;

            var ldCtrlWrite = new LoadProcedure_tLdCtrlWriteRelMem();
            ldCtrlWrite.ObjIdx = 4;
            ldCtrlWrite.ObjIdxSpecified = true;
            ldCtrlWrite.Offset = 0;
            ldCtrlWrite.Verify = true;
            ldCtrlWrite.Size = _codeSegment.Size;
            ldProc2.Items.Add(ldCtrlWrite);

            var appStatic = _applicationProgram.Static;
            appStatic.LoadProcedures.Clear();
            appStatic.LoadProcedures.Add(ldProc1);
            appStatic.LoadProcedures.Add(ldProc2);
        }

        private void HandleParameters()
        {
            var appStatic = _applicationProgram.Static;
            appStatic.Parameters.Clear();
            appStatic.ParameterRefs.Clear();

            uint offset = 0;
            uint size = 0;
            foreach (var item in Parameters)
            {
                appStatic.Parameters.Add(item);
                item.Item = new Parameter_tMemory() { Offset = offset, BitOffset = 0 };
                offset += item.Type.SizeInByte;
                size += item.Type.SizeInByte;

                appStatic.ParameterRefs.Add(new ParameterRef_t() { Parameter = item });
            }
            _codeSegment.Size = size;
        }

        private void SetEmptyListsNull()
        {
            var appStatic = _applicationProgram.Static;

            if (appStatic.ParameterCalculations?.Count() == 0)
                appStatic.ParameterCalculations = null;

            if (appStatic.ParameterValidations?.Count() == 0)
                appStatic.ParameterValidations = null;

            if (appStatic.FixupList?.Count() == 0)
                appStatic.FixupList = null;

            if (appStatic.BinaryData?.Count() == 0)
                appStatic.BinaryData = null;

            if (appStatic.Messages?.Count() == 0)
                appStatic.Messages = null;

            if (appStatic.SecurityRoles?.Count() == 0)
                appStatic.SecurityRoles = null;

            if (appStatic.BusInterfaces?.Count() == 0)
                appStatic.BusInterfaces = null;

            if (_manufacturerData.Baggages?.Count() == 0)
                _manufacturerData.Baggages = null;

            if (_manufacturerData.Languages?.Count() == 0)
                _manufacturerData.Languages = null;

            if (_product.Baggages?.Count() == 0)
                _product.Baggages = null;

            if (_product.Attributes?.Count() == 0)
                _product.Attributes = null;
        }

        private void RegenerateLoadProcedure()
        {
            var ldProc1 = new LoadProcedures_tLoadProcedure();
            ldProc1.MergeId = 2;
            ldProc1.MergeIdSpecified = true;

            var ldCtrlCreate = new LoadProcedure_tLdCtrlRelSegment();
            ldCtrlCreate.LsmIdx = 4;
            ldCtrlCreate.LsmIdxSpecified = true;
            ldCtrlCreate.Mode = 0;
            ldCtrlCreate.Fill = 0;
            ldCtrlCreate.Size = 0;
            ldProc1.Items.Add(ldCtrlCreate);

            var ldProc2 = new LoadProcedures_tLoadProcedure();
            ldProc2.MergeId = 4;
            ldProc2.MergeIdSpecified = true;

            var ldCtrlWrite = new LoadProcedure_tLdCtrlWriteRelMem();
            ldCtrlWrite.ObjIdx = 4;
            ldCtrlWrite.ObjIdxSpecified = true;
            ldCtrlWrite.Offset = 0;
            ldCtrlWrite.Verify = true;
            ldCtrlWrite.Size = 0;
            ldProc2.Items.Add(ldCtrlWrite);

            var appStatic = _applicationProgram.Static;

            appStatic.LoadProcedures.Clear();
            appStatic.LoadProcedures.Add(ldProc1);
            appStatic.LoadProcedures.Add(ldProc2);
        }

        private void RegenerateDynamic()
        {
            var appDynamic = _applicationProgram.Dynamic;
            var appStatic = _applicationProgram.Static;
            appDynamic.Clear();

            var commonChannel = new ApplicationProgramDynamic_tChannelIndependentBlock();
            _parameterBlock = new ComObjectParameterBlock_t();
            _parameterBlock.Name = "ParameterPage";
            _parameterBlock.Text = Ressources.CommonParameters;

            foreach(var paramRef in appStatic.ParameterRefs)
            {
                _parameterBlock.Items.Add(new ParameterRefRef_t() { ParameterRef = paramRef });
            }

            foreach (var comObjRef in appStatic.ComObjectRefs)
            {
                _parameterBlock.Items.Add(new ComObjectRefRef_t() { ComObjectRef = comObjRef });
            }

            commonChannel.Items.Add(_parameterBlock);
            appDynamic.Add(commonChannel);
        }

        private bool AskSaveCancel()
        {
            var result = _dialogService.Ask(Ressources.AskSave);
            if (result == null)
                return true;

            if (result.Value)
                Save(null);

            return false;
        }

        private void ClearData()
        {
            _model = null;
            _openFile = null;
            _manufacturerData = null;
            _hardware = null;
            _product = null;
            _catalogItem = null;
            _catalogSection = null;
            _applicationProgram = null;
            _hardware2Program = null;
            _appProgRef = null;
            _codeSegment = null;
            _parameterBlock = null;
            Parameters.Clear();
            RaiseChanged();
        }

        private void Close(object param)
        {
            try
            {
                if (_model == null)
                    return;

                var cancel = AskSaveCancel();
                if (cancel)
                    return;

                ClearData();
            }
            catch (Exception ex)
            {
                _dialogService.ShowMessage(ex.ToString());
            }
        }

        private void CreateNew(object param)
        {
            try
            {
                if (_model != null)
                {
                    var cancel = AskSaveCancel();
                    if (cancel)
                        return;
                }

                _model = new KNX();

                string lang = Thread.CurrentThread.CurrentCulture.Name;

                _manufacturerData = new ManufacturerData_tManufacturer();
                _applicationProgram = new ApplicationProgram_t();
                _hardware = new Hardware_t();
                _catalogSection = new CatalogSection_t();
                _product = new Hardware_tProduct();
                _hardware2Program = new Hardware2Program_t();
                _appProgRef = new ApplicationProgramRef_t();
                _catalogItem = new CatalogSection_tCatalogItem();

                _model.ManufacturerData.Add(_manufacturerData);
                _manufacturerData.Catalog.Add(_catalogSection);
                _manufacturerData.ApplicationPrograms.Add(_applicationProgram);
                _manufacturerData.Hardware.Add(_hardware);
                _hardware.Products.Add(_product);
                _hardware.Hardware2Programs.Add(_hardware2Program);
                _hardware2Program.ApplicationProgramRef.Add(_appProgRef);
                _catalogSection.CatalogItem.Add(_catalogItem);


                _model.CreatedBy = _toolName;
                _model.ToolVersion = _toolVersion;

                ApplicationNumber = 0;
                ApplicationVersion = 0;
                _applicationProgram.ProgramType = ApplicationProgramType_t.ApplicationProgram;
                _applicationProgram.MaskVersion = "MV-57B0";
                _applicationProgram.LoadProcedureStyle = LoadProcedureStyle_t.MergedProcedure;
                _applicationProgram.PeiType = 0;
                _applicationProgram.DefaultLanguage = lang;
                _applicationProgram.DynamicTableManagement = false;
                _applicationProgram.Linkable = false;
                _applicationProgram.MinEtsVersion = "4.0";

                var appStatic = new ApplicationProgramStatic_t();
                _applicationProgram.Static = appStatic;

                var code = new ApplicationProgramStatic_tCode();
                appStatic.Code = code;

                _codeSegment = new ApplicationProgramStatic_tCodeRelativeSegment();
                code.RelativeSegment.Add(_codeSegment);
                _codeSegment.Name = "Parameters";
                _codeSegment.Offset = 0;
                _codeSegment.LoadStateMachine = 4;
                _codeSegment.Size = 0;

                appStatic.AddressTable = new ApplicationProgramStatic_tAddressTable();
                appStatic.AddressTable.MaxEntries = 255;

                appStatic.AssociationTable = new ApplicationProgramStatic_tAssociationTable();
                appStatic.AssociationTable.MaxEntries = 255;
                appStatic.ComObjectTable = new ApplicationProgramStatic_tComObjectTable();
                appStatic.Options = new ApplicationProgramStatic_tOptions();

                HardwareSerial = "0";
                HardwareVersion = 0;
                _hardware.HasIndividualAddress = true;
                _hardware.HasApplicationProgram = true;
                _hardware.IsIPEnabled = true;

                _product.IsRailMounted = false;
                _product.DefaultLanguage = lang;
                OrderNumber = "0";

                _hardware2Program.MediumTypes.Add("MT-5");

                _catalogSection.Name = Ressources.Devices;
                _catalogSection.Number = "1";
                _catalogSection.DefaultLanguage = lang;

                _catalogItem.Name = _product.Text;
                _catalogItem.Number = 1;
                _catalogItem.ProductRefId = _product.Id;
                _catalogItem.Hardware2ProgramRefId = _hardware2Program.Id;
                _catalogItem.DefaultLanguage = lang;

                RaiseChanged();
            }
            catch (Exception ex)
            {
                _dialogService.ShowMessage(ex.ToString());
                ClearData();
            }
        }

        private void CorrectIds()
        {
            _manufacturerData.RefId = "M-00FA";
            _applicationProgram.Id = string.Format("{0}_A-{1:X04}-{2:X02}-0000", _manufacturerData.RefId,
                    _applicationProgram.ApplicationNumber, _applicationProgram.ApplicationVersion);
            _appProgRef.RefId = _applicationProgram.Id;
            _codeSegment.Id = string.Format("{0}_RS-{1:00}-{2:00000}", _applicationProgram.Id, _codeSegment.LoadStateMachine,
                _codeSegment.Offset);
            
            _hardware.Id = string.Format("{0}_H-{1}-{2}", _manufacturerData.RefId, _hardware.SerialNumber,
                                        _hardware.VersionNumber);
            _product.Id = string.Format("{0}_P-{1}", _hardware.Id, _product.OrderNumber);
            _hardware2Program.Id = string.Format("{0}_HP-{1:X04}-{2:X02}-0000", _hardware.Id,
                        _applicationProgram.ApplicationNumber, _applicationProgram.ApplicationVersion);

            _catalogSection.Id = string.Format("{0}_CS-{1}", _manufacturerData.RefId, _catalogSection.Number);

            _catalogItem.Id = string.Format("{0}_CI-{1}-{2}", _hardware2Program.Id, _product.OrderNumber, _catalogItem.Number);
            _catalogItem.ProductRefId = _product.Id;
            _catalogItem.Hardware2ProgramRefId = _hardware2Program.Id;

            var appStatic = _applicationProgram.Static;

            foreach (var paraType in appStatic.ParameterTypes)
            {
                paraType.Id = string.Format("{0}_PT-{1}", _applicationProgram.Id, paraType.Name);

                var paraTypeRestriction = paraType.Item as ParameterType_tTypeRestriction;
                if (paraTypeRestriction == null)
                    continue;

                foreach (var paraEnum in paraTypeRestriction.Enumeration)
                    paraEnum.Id = string.Format("{0}_EN-{1}", paraType.Id, paraEnum.Value);
            }

            int i = 1;
            foreach (var parameter in appStatic.Parameters.OfType<Parameter_t>())
            {
                parameter.ParameterType = parameter.Type.Id;
                parameter.Id = string.Format("{0}_P-{1}", _applicationProgram.Id, i++);
                var memory = parameter.Item as Parameter_tMemory;
                if(memory != null)
                {
                    memory.CodeSegment = _codeSegment.Id;
                }
            }

            i = 1;
            foreach(var parameterRef in appStatic.ParameterRefs)
            {
                parameterRef.Id = string.Format("{0}_R-{1}", parameterRef.Parameter.Id, i++);
                parameterRef.RefId = parameterRef.Parameter.Id;
            }

            foreach(var comObj in appStatic.ComObjectTable.ComObject)
            {
                comObj.Id = $"{_applicationProgram.Id}_O-{comObj.Number}";
            }

            i = 1;
            foreach(var comObjRef in appStatic.ComObjectRefs)
            {
                comObjRef.Id = $"{comObjRef.ComObject.Id}_R-{i++}";
                comObjRef.RefId = comObjRef.ComObject.Id;
            }

            _parameterBlock.Id = string.Format("{0}_PB-1", _applicationProgram.Id);
            foreach (var item in _parameterBlock.Items.OfType<ParameterRefRef_t>())
                item.RefId = item.ParameterRef.Id;

            foreach (var item in _parameterBlock.Items.OfType<ComObjectRefRef_t>())
                item.RefId = item.ComObjectRef.Id;
        }

        private void Export(object param)
        {
            try
            {
                if (_model == null)
                    return;

                var cancel = AskSaveCancel();
                if (cancel)
                    return;

                var files = new string[] { _openFile };

                var outputFile = _dialogService.ChooseSaveFile(".knxprod", "KNXProd|*.knxprod");
                if (outputFile == null)
                    return;

                var asmPath = Path.Combine(Properties.Settings.Default.ETSPath, "Knx.Ets.Converter.ConverterEngine.dll");
                var asm = Assembly.LoadFrom(asmPath);
                var eng = asm.GetType("Knx.Ets.Converter.ConverterEngine.ConverterEngine");
                var bas = asm.GetType("Knx.Ets.Converter.ConverterEngine.ConvertBase");

                //ConvertBase.Uninitialize();
                InvokeMethod(bas, "Uninitialize", null);

                //var dset = ConverterEngine.BuildUpRawDocumentSet( files );
                var dset = InvokeMethod(eng, "BuildUpRawDocumentSet", new object[] { files });

                //ConverterEngine.CheckOutputFileName(outputFile, ".knxprod");
                InvokeMethod(eng, "CheckOutputFileName", new object[] { outputFile, ".knxprod" });

                //ConvertBase.CleanUnregistered = false;
                //SetProperty(bas, "CleanUnregistered", false);

                //dset = ConverterEngine.ReOrganizeDocumentSet(dset);
                dset = InvokeMethod(eng, "ReOrganizeDocumentSet", new object[] { dset });

                //ConverterEngine.PersistDocumentSetAsXmlOutput(dset, outputFile, null, string.Empty, true, _toolName, _toolVersion);
                InvokeMethod(eng, "PersistDocumentSetAsXmlOutput", new object[] { dset, outputFile, null,
                            "", true, _toolName, _toolVersion });

                _dialogService.ShowMessage(Ressources.ExportSuccess);
            }
            catch (Exception ex)
            {
                _dialogService.ShowMessage(ex.ToString());
            }
        }

        private void RaiseChanged()
        {
            RaisePropertyChanged(nameof(EditEnabled));
            RaisePropertyChanged(nameof(HardwareName));
            RaisePropertyChanged(nameof(HardwareSerial));
            RaisePropertyChanged(nameof(HardwareVersion));
            RaisePropertyChanged(nameof(ProductName));
            RaisePropertyChanged(nameof(OrderNumber));
            RaisePropertyChanged(nameof(ApplicationName));
            RaisePropertyChanged(nameof(ApplicationNumber));
            RaisePropertyChanged(nameof(ApplicationVersion));
            RaisePropertyChanged(nameof(ParameterTypes));
            RaisePropertyChanged(nameof(Parameters));
            RaisePropertyChanged(nameof(ComObjects));
            RaisePropertyChanged(nameof(MediumType));
            RaisePropertyChanged(nameof(ReplacedVersions));
        }

        #region Properties
        public bool EditEnabled
        {
            get => _model != null;
        }

        public string HardwareName
        {
            get
            {
                return _hardware.Name;
            }
            set
            {
                _hardware.Name = value;
                RaisePropertyChanged(nameof(HardwareName));
            }
        }

        public string HardwareSerial
        {
            get
            {
                return _hardware.SerialNumber;
            }
            set
            {
                _hardware.SerialNumber = value;
                RaisePropertyChanged(nameof(HardwareSerial));
            }
        }

        public ushort HardwareVersion
        {
            get => _hardware.VersionNumber;
            set
            {
                _hardware.VersionNumber = value;
                RaisePropertyChanged(nameof(HardwareVersion));
            }
        }

        public string ProductName
        {
            get
            {
                return _product.Text;
            }
            set
            {
                _product.Text = value;
                _catalogItem.Name = value;
                RaisePropertyChanged(nameof(ProductName));
            }
        }

        public string OrderNumber
        {
            get
            {
                return _product.OrderNumber;
            }
            set
            {
                _product.OrderNumber = value;
                RaisePropertyChanged(nameof(OrderNumber));
            }
        }

        public string ApplicationName
        {
            get
            {
                return _applicationProgram.Name;
            }
            set
            {
                _applicationProgram.Name = value;
                RaisePropertyChanged(nameof(ApplicationName));
            }
        }

        public ushort ApplicationNumber
        {
            get
            {
                return _applicationProgram.ApplicationNumber;
            }
            set
            {
                _applicationProgram.ApplicationNumber = value;
                RaisePropertyChanged(nameof(ApplicationNumber));
            }
        }

        public byte ApplicationVersion
        {
            get
            {
                return _applicationProgram.ApplicationVersion;
            }
            set
            {
                _applicationProgram.ApplicationVersion = value;
                RaisePropertyChanged(nameof(ApplicationVersion));
            }
        }

        public string ReplacedVersions
        {
            get
            {
                if (_applicationProgram == null)
                    return "";

                return _applicationProgram.ReplacesVersions;
                //return string.Join(" ", _applicationProgram.ReplacesVersions.Select(b => b.ToString()));
            }
            set
            {
                _applicationProgram.ReplacesVersions = value;
                if (string.IsNullOrWhiteSpace(value))
                    _applicationProgram.ReplacesVersions = null;
                //try
                //{
                //    _applicationProgram.ReplacesVersions = value.Split(' ').Select(s => byte.Parse(s)).ToArray();
                //}
                //catch(Exception ex)
                //{
                //    _applicationProgram.ReplacesVersions = null;
                //}
                                  
                RaisePropertyChanged(nameof(ReplacedVersions));
            }
        }

        public ObservableCollection<ParameterType_t> ParameterTypes
        {
            get => _applicationProgram?.Static?.ParameterTypes;
        }

        public ObservableCollection<Parameter_t> Parameters { get; private set; } = new ObservableCollection<Parameter_t>();

        public ObservableCollection<ComObject_t> ComObjects
        {
            get => _applicationProgram?.Static?.ComObjectTable?.ComObject;
        }

        public string MediumType
        {
            get => _hardware2Program.MediumTypes[0];
            set
            {
                _hardware2Program.MediumTypes[0] = value;
                RaisePropertyChanged(nameof(MediumType));
            }
        }

        #endregion

        #region Reflection
        private object InvokeMethod(Type type, string methodName, object[] args)
        {

            var mi = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
            return mi.Invoke(null, args);
        }

        private void SetProperty(Type type, string propertyName, object value)
        {
            PropertyInfo prop = type.GetProperty(propertyName, BindingFlags.NonPublic | BindingFlags.Static);
            prop.SetValue(null, value, null);
        }
        #endregion

        #region Commands

        public ICommand ExportCommand
        {
            get => _exportCommand;
        }

        public ICommand CreateNewCommand
        {
            get => _createNewCommand;
        }

        public ICommand OpenCommand
        {
            get => _openCommand;
        }

        public ICommand CloseCommand
        {
            get => _closeCommand;
        }

        public ICommand SaveCommand
        {
            get => _saveCommand;
        }

        public ICommand SaveAsCommand
        {
            get => _saveAsCommand;
        }

        #endregion

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;
        public void RaisePropertyChanged(string property)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(property));
        }
        #endregion
    }
}
