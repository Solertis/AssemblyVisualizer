﻿// Copyright 2011 Denis Markelov
// This code is distributed under Microsoft Public License 
// (for details please see \docs\Ms-PL)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AssemblyVisualizer.Infrastructure;
using AssemblyVisualizer.Common;
using AssemblyVisualizer.HAL;
using System.Collections.ObjectModel;
using AssemblyVisualizer.Properties;
using AssemblyVisualizer.Model;
using System.Windows.Input;
using AssemblyVisualizer.Controls.Graph.QuickGraph;
using System.Windows.Media;

namespace AssemblyVisualizer.InteractionBrowser
{
    class InteractionBrowserWindowViewModel : ViewModelBase
    {
        private Dictionary<MemberInfo, MemberViewModel> _viewModelsDictionary = new Dictionary<MemberInfo, MemberViewModel>();
        private MemberGraph _graph;
        private IEnumerable<TypeInfo> _types;
        private IEnumerable<IEnumerable<TypeViewModel>> _hierarchies;
        private IDictionary<TypeInfo, TypeViewModel> _viewModelCorrespondence = new Dictionary<TypeInfo, TypeViewModel>();
        private bool _isTypeSelectionVisible;
        private bool _showUnconnectedVertices;

        public InteractionBrowserWindowViewModel(IEnumerable<TypeInfo> types, bool drawGraph)
        {
            _types = types;

            ApplySelectionCommand = new DelegateCommand(ApplySelectionCommandHandler);
            ShowSelectionViewCommand = new DelegateCommand(ShowSelectionViewCommandHandler);
            HideSelectionViewCommand = new DelegateCommand(HideSelectionViewCommandHandler);
            ToggleSelectionViewCommand = new DelegateCommand(ToggleSelectionViewCommandHandler);
            Commands = new ObservableCollection<UserCommand>
			           	{
			           		new UserCommand(Resources.FillGraph, OnFillGraphRequest),
			           		new UserCommand(Resources.OriginalSize, OnOriginalSizeRequest),	
                            new UserCommand(Resources.SelectTypes, ShowSelectionViewCommand)
                            //new UserCommand(Resources.SearchInGraph, ShowSearchCommand),                                                    
			           	};

            _hierarchies = types
                .Select(GetHierarchy)
                .Select(h => h.Select(GetViewModelForType).ToArray())
                .ToArray();

            if (_hierarchies.Count() > 1)
            {
                foreach (var hierarchy in _hierarchies)
                {
                    hierarchy.First().IsSelected = true;
                }
            }
            else
            {
                var hierarchy = _hierarchies.Single();
                foreach (var type in hierarchy)
                {
                    type.IsSelected = true;
                    type.ShowInternals = true;
                }
            }

            if (drawGraph)
            {
                DrawGraph();
            }
            else
            {
                _isTypeSelectionVisible = true;
            }
        }

        public event Action FillGraphRequest;
        public event Action OriginalSizeRequest;
        //public event Action FocusSearchRequest;

        public IEnumerable<UserCommand> Commands { get; private set; }

        public ICommand ApplySelectionCommand { get; private set; }
        public ICommand ShowSelectionViewCommand { get; private set; }
        public ICommand HideSelectionViewCommand { get; private set; }
        public ICommand ToggleSelectionViewCommand { get; private set; }

        public IEnumerable<IEnumerable<TypeViewModel>> Hierarchies
        {
            get
            {
                return _hierarchies;
            }
            set
            {
                _hierarchies = value;
                OnPropertyChanged("Hierarchies");
            }
        }

        public IEnumerable<TypeViewModel> DisplayedTypes
        {
            get
            {
                return _hierarchies
                    .SelectMany(h => h.Where(t => t.IsSelected))
                    .Distinct()
                    .ToArray();
            }
        }

        public MemberGraph Graph
        {
            get
            {
                return _graph;
            }
            set
            {
                _graph = value;
                OnPropertyChanged("Graph");
            }
        }

        public int MembersCount
        {
            get
            {
                var graph = CreateGraph(DisplayedTypes);
                return graph.Vertices.Count();
            }
        }

        public bool IsTypeSelectionVisible
        {
            get
            {
                return _isTypeSelectionVisible;
            }
            set
            {
                _isTypeSelectionVisible = value;
                OnPropertyChanged("IsTypeSelectionVisible");
            }
        }

        public bool ShowUnconnectedVertices
        {
            get
            {
                return _showUnconnectedVertices;
            }
            set
            {
                _showUnconnectedVertices = value;
                OnPropertyChanged("ShowUnconnectedVertices");
                ReportSelectionChanged();
            }
        }

        public string Title
        {
            get
            {
                return Resources.InteractionBrowser;
            }
        }

        public void ReportSelectionChanged()
        {
            OnPropertyChanged("MembersCount");
        }

        private TypeViewModel GetViewModelForType(TypeInfo typeInfo)
        {
            if (_viewModelCorrespondence.ContainsKey(typeInfo))
            {
                return _viewModelCorrespondence[typeInfo];
            }
            var viewModel = new TypeViewModel(typeInfo, this);
            _viewModelCorrespondence.Add(typeInfo, viewModel);
            return viewModel;
        }

        private List<TypeInfo> GetHierarchy(TypeInfo typeInfo)
        {
            var hierarchy = new List<TypeInfo>();
            var currentType = typeInfo;
            hierarchy.Add(typeInfo);
            while (currentType.BaseType != null)
            {
                var t = currentType.BaseType;
                hierarchy.Add(t);
                currentType = t;
            }
            return hierarchy;
        }

        private MemberGraph CreateGraph(IEnumerable<TypeViewModel> typeViewModels)
        {
            var graph = new MemberGraph(true);

            foreach (var typeViewModel in typeViewModels)
            {
                var type = typeViewModel.TypeInfo;
                var methods = type.Methods.Where(m => !m.Name.StartsWith("<")).Concat(type.Accessors);
                if (!typeViewModel.ShowInternals)
                {
                    methods = methods.Where(m => m.IsVisibleOutside());
                }

                foreach (var method in methods)
                {
                    var mvm = GetViewModelForMethod(method);
                    if (!graph.ContainsVertex(mvm))
                    {
                        graph.AddVertex(mvm);
                    }

                    var usedMethods = Helper.GetUsedMethods(method.MemberReference)
                        .Where(m => typeViewModels.Any(t => t.TypeInfo == m.DeclaringType && (m.IsVisibleOutside() || t.ShowInternals)) && !m.Name.StartsWith("<"))
                        .ToArray();
                    foreach (var usedMethod in usedMethods)
                    {
                        var vm = GetViewModelForMethod(usedMethod);
                        if (!graph.ContainsVertex(vm))
                        {
                            graph.AddVertex(vm);
                        }
                        graph.AddEdge(new Edge<MemberViewModel>(mvm, vm));
                    }

                    var usedFields = Helper.GetUsedFields(method.MemberReference)
                        .Where(m => typeViewModels.Any(
                            tvm => tvm.TypeInfo == m.DeclaringType && (m.IsVisibleOutside() || tvm.ShowInternals)) && !m.Name.StartsWith("CS$") && !m.Name.EndsWith("k__BackingField"))
                        .ToArray();
                    foreach (var usedField in usedFields)
                    {
                        var vm = GetViewModelForField(usedField);
                        if (!graph.ContainsVertex(vm))
                        {
                            graph.AddVertex(vm);
                        }
                        graph.AddEdge(new Edge<MemberViewModel>(mvm, vm));
                    }
                }
            }

            if (!ShowUnconnectedVertices)
            {
                graph.RemoveVertexIf(v => graph.Degree(v) == 0);
            }            

            return graph;
        }

        private MemberViewModel GetViewModelForField(FieldInfo fieldInfo)
        {
            MemberViewModel vm;
            if (_viewModelsDictionary.ContainsKey(fieldInfo))
            {
                var typeViewModel = GetViewModelForType(fieldInfo.DeclaringType);
                vm = _viewModelsDictionary[fieldInfo];
                vm.Background = typeViewModel.Background;
                return vm;
            }   

            var eventInfo = Helper.GetEventForBackingField(fieldInfo.MemberReference);
            if (eventInfo != null)
            {
                var typeViewModel = GetViewModelForType(eventInfo.DeclaringType);
                if (_viewModelsDictionary.ContainsKey(eventInfo))
                {
                    vm = _viewModelsDictionary[eventInfo];
                    vm.Background = typeViewModel.Background;
                    return vm;
                }                
                var evm = new EventViewModel(eventInfo)
                {
                    Background = typeViewModel.Background,
                    ToolTip = typeViewModel.Name
                };
                _viewModelsDictionary.Add(eventInfo, evm);
                return evm;
            }

            var tvm = GetViewModelForType(fieldInfo.DeclaringType);
            var fvm = new FieldViewModel(fieldInfo)
            {
                Background = tvm.Background,
                ToolTip = tvm.Name
            };
            _viewModelsDictionary.Add(fieldInfo, fvm);
            return fvm;
        }

        private MemberViewModel GetViewModelForMethod(MethodInfo methodInfo)
        {
            MemberViewModel vm;
            if (_viewModelsDictionary.ContainsKey(methodInfo))
            {
                var typeViewModel = GetViewModelForType(methodInfo.DeclaringType);
                vm = _viewModelsDictionary[methodInfo];
                vm.Background = typeViewModel.Background;
                return vm;
            }            

            if (IsPropertyAccessor(methodInfo))
            {
                var propertyInfo = Helper.GetAccessorProperty(methodInfo.MemberReference);
                var typeViewModel = GetViewModelForType(propertyInfo.DeclaringType);
                
                if (_viewModelsDictionary.ContainsKey(propertyInfo))
                {
                    vm = _viewModelsDictionary[propertyInfo];
                    vm.Background = typeViewModel.Background;
                    return vm;
                }                
                var pvm = new PropertyViewModel(propertyInfo)
                {
                    Background = typeViewModel.Background,
                    ToolTip = typeViewModel.Name
                };
                _viewModelsDictionary.Add(propertyInfo, pvm);
                return pvm;
            }
            if (IsEventAccessor(methodInfo))
            {
                var eventInfo = Helper.GetAccessorEvent(methodInfo.MemberReference);
                var typeViewModel = GetViewModelForType(eventInfo.DeclaringType);
                if (_viewModelsDictionary.ContainsKey(eventInfo))
                {
                    vm = _viewModelsDictionary[eventInfo];
                    vm.Background = typeViewModel.Background;
                    return vm;
                }                
                var evm = new EventViewModel(eventInfo)
                {
                    Background = typeViewModel.Background,
                    ToolTip = typeViewModel.Name
                };
                _viewModelsDictionary.Add(eventInfo, evm);
                return evm;
            }

            var tvm = GetViewModelForType(methodInfo.DeclaringType);
            var mvm = new MethodViewModel(methodInfo)
            {
                Background = tvm.Background,
                ToolTip = tvm.Name
            };
            _viewModelsDictionary.Add(methodInfo, mvm);
            return mvm;
        }       

        private void OnFillGraphRequest()
        {
            var handler = FillGraphRequest;

            if (handler != null)
            {
                handler();
            }
        }

        private void ApplySelectionCommandHandler()
        {
            if (!IsTypeSelectionVisible)
            {
                return;
            }
            IsTypeSelectionVisible = false;
            DrawGraph();
        }

        private void DrawGraph()
        {
            var types = DisplayedTypes;
            ColorizeTypes(types);
            Graph = CreateGraph(types);
            OnPropertyChanged("DisplayedTypes");
        }

        private void HideSelectionViewCommandHandler()
        {
            IsTypeSelectionVisible = false;
        }

        private void ShowSelectionViewCommandHandler()
        {
            IsTypeSelectionVisible = true;
        }

        private void ToggleSelectionViewCommandHandler()
        {
            IsTypeSelectionVisible = !IsTypeSelectionVisible;
        }

        private void OnOriginalSizeRequest()
        {
            var handler = OriginalSizeRequest;

            if (handler != null)
            {
                handler();
            }
        }

        private static bool IsPropertyAccessor(MethodInfo method)
        {
            return (method.IsSpecialName
                    && (method.Name.IndexOf("get_") != -1 || method.Name.IndexOf("set_") != -1));
        }

        private static bool IsEventAccessor(MethodInfo method)
        {
            return (method.IsSpecialName
                    && (method.Name.IndexOf("add_") != -1 || method.Name.IndexOf("remove_") != -1));
        }

        private static void ColorizeTypes(IEnumerable<TypeViewModel> types)
        {
            int index = 0;
            foreach (var type in types)
            {
                type.Background = BrushProvider.SingleBrushes[index];
                index++;
                if (index >= BrushProvider.SingleBrushes.Count)
                {
                    index = 0;
                }
            }
        }
    }
}