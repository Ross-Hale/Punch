//====================================================================================================================
// Copyright (c) 2012 IdeaBlade
//====================================================================================================================
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE 
// WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS 
// OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR 
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE. 
//====================================================================================================================
// USE OF THIS SOFTWARE IS GOVERENED BY THE LICENSING TERMS WHICH CAN BE FOUND AT
// http://cocktail.ideablade.com/licensing
//====================================================================================================================

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Windows.Threading;
using Caliburn.Micro;
using Cocktail;
using Common.Errors;
using Common.Messages;
using Common.Repositories;
using Common.Toolbar;
using Common.Workspace;
using DomainModel.Projections;
using IdeaBlade.EntityModel;
using Action = System.Action;

namespace TempHire.ViewModels.StaffingResource
{
    [Export]
    public class StaffingResourceManagementViewModel : Conductor<IScreen>, IDiscoverableViewModel, IHandle<EntityChangedMessage>,
                                               IWorkspace
    {
        private readonly ExportFactory<StaffingResourceDetailViewModel> _detailFactory;
        private readonly IDialogManager _dialogManager;
        private readonly IErrorHandler _errorHandler;
        private readonly ExportFactory<StaffingResourceNameEditorViewModel> _nameEditorFactory;
        private readonly IRepositoryManager<IStaffingResourceRepository> _repositoryManager;
        private readonly DispatcherTimer _selectionChangeTimer;
        private readonly IToolbarManager _toolbar;
        private IScreen _retainedActiveItem;
        private ToolbarGroup _toolbarGroup;

        [ImportingConstructor]
        public StaffingResourceManagementViewModel(StaffingResourceSearchViewModel searchPane,
                                           ExportFactory<StaffingResourceDetailViewModel> detailFactory,
                                           ExportFactory<StaffingResourceNameEditorViewModel> nameEditorFactory,
                                           IRepositoryManager<IStaffingResourceRepository> repositoryManager,
                                           IErrorHandler errorHandler, IDialogManager dialogManager,
                                           IToolbarManager toolbar)
        {
            SearchPane = searchPane;
            _detailFactory = detailFactory;
            _nameEditorFactory = nameEditorFactory;
            _repositoryManager = repositoryManager;
            _errorHandler = errorHandler;
            _dialogManager = dialogManager;
            _toolbar = toolbar;

            EventFns.Subscribe(this);

            PropertyChanged += OnPropertyChanged;

            // ReSharper disable DoNotCallOverridableMethodsInConstructor
            DisplayName = "Resource Management";
            // ReSharper restore DoNotCallOverridableMethodsInConstructor

            _selectionChangeTimer = new DispatcherTimer { Interval = new TimeSpan(0, 0, 0, 0, 200) };
            _selectionChangeTimer.Tick += OnSelectionChangeElapsed;
        }

        public StaffingResourceSearchViewModel SearchPane { get; private set; }

        public bool CanDelete
        {
            get { return SearchPane.CurrentStaffingResource != null; }
        }

        private IStaffingResourceRepository ActiveRepository
        {
            get { return _repositoryManager.GetRepository(ActiveStaffingResource.Id); }
        }

        private StaffingResourceDetailViewModel ActiveDetail
        {
            get { return ActiveItem as StaffingResourceDetailViewModel; }
        }

        private DomainModel.StaffingResource ActiveStaffingResource
        {
            get { return ActiveDetail != null ? ActiveDetail.StaffingResource : null; }
        }

        public bool CanSave
        {
            get
            {
                return ActiveStaffingResource != null && ActiveRepository.HasChanges() &&
                       !ActiveStaffingResource.EntityFacts.EntityState.IsDeleted();
            }
        }

        public bool CanCancel
        {
            get { return CanSave; }
        }

        #region IHandle<EntityChangedMessage> Members

        public void Handle(EntityChangedMessage message)
        {
            NotifyOfPropertyChange(() => CanSave);
            NotifyOfPropertyChange(() => CanCancel);
        }

        #endregion

        #region IWorkspace Members

        public bool IsDefault
        {
            get { return false; }
        }

        public int Sequence
        {
            get { return 10; }
        }

        #endregion

        private void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "ActiveItem")
            {
                if (_retainedActiveItem != null)
                    _retainedActiveItem.PropertyChanged -= OnActiveDetailPropertyChanged;

                _retainedActiveItem = ActiveItem;
                if (ActiveItem != null)
                    ActiveItem.PropertyChanged += OnActiveDetailPropertyChanged;
            }
        }

        private void OnActiveDetailPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "StaffingResource")
            {
                NotifyOfPropertyChange(() => CanSave);
                NotifyOfPropertyChange(() => CanCancel);
            }
        }

        public StaffingResourceManagementViewModel Start()
        {
            SearchPane.Start();
            return this;
        }

        protected override void OnActivate()
        {
            base.OnActivate();

            Start();
            SearchPane.PropertyChanged += OnSearchPanePropertyChanged;
            ((IActivate)SearchPane).Activate();

            if (_toolbarGroup == null)
            {
                _toolbarGroup = new ToolbarGroup(10)
                                    {
                                        new ToolbarAction(this, "Add", (Func<IEnumerable<IResult>>) Add),
                                        new ToolbarAction(this, "Delete", (Func<IEnumerable<IResult>>) Delete),
                                        new ToolbarAction(this, "Save", (Func<IEnumerable<IResult>>) Save),
                                        new ToolbarAction(this, "Cancel", (Action) Cancel)
                                    };
            }
            _toolbar.AddGroup(_toolbarGroup);
        }

        private void OnSelectionChangeElapsed(object sender, EventArgs e)
        {
            _selectionChangeTimer.Stop();

            if (SearchPane.CurrentStaffingResource != null)
            {
                Func<StaffingResourceDetailViewModel> target = () => ActiveDetail ?? _detailFactory.CreateExport().Value;
                new NavigateResult<StaffingResourceDetailViewModel>(this, target)
                    {
                        Prepare = nav => nav.Target.Start(SearchPane.CurrentStaffingResource.Id)
                    }
                    .Go();
            }

            NotifyOfPropertyChange(() => CanDelete);
        }

        private void OnSearchPanePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "CurrentStaffingResource") return;

            if (_selectionChangeTimer.IsEnabled) _selectionChangeTimer.Stop();
            _selectionChangeTimer.Start();
        }

        protected override void OnDeactivate(bool close)
        {
            if (close)
                ActiveItem = null;

            base.OnDeactivate(close);
            SearchPane.PropertyChanged -= OnSearchPanePropertyChanged;
            ((IDeactivate)SearchPane).Deactivate(close);

            _toolbar.RemoveGroup(_toolbarGroup);
        }

        public IEnumerable<IResult> Add()
        {
            StaffingResourceNameEditorViewModel nameEditor = _nameEditorFactory.CreateExport().Value;
            yield return _dialogManager.ShowDialog(nameEditor, DialogButtons.OkCancel);

            SearchPane.CurrentStaffingResource = null;

            Func<StaffingResourceDetailViewModel> target = () => ActiveDetail ?? _detailFactory.CreateExport().Value;
            yield return
                new NavigateResult<StaffingResourceDetailViewModel>(this, target)
                    {
                        Prepare = nav =>
                                  nav.Target.Start(nameEditor.FirstName, nameEditor.MiddleName, nameEditor.LastName)
                    };
        }

        public IEnumerable<IResult> Delete()
        {
            StaffingResourceListItem staffingResource = SearchPane.CurrentStaffingResource;

            yield return
                _dialogManager.ShowMessage(string.Format("Are you sure you want to delete {0}?", staffingResource.FullName),
                                           DialogResult.No, DialogButtons.YesNo);

            using (ActiveDetail.Busy.GetTicket())
            {
                IStaffingResourceRepository repository = _repositoryManager.GetRepository(staffingResource.Id);

                OperationResult operation =
                    repository.DeleteStaffingResourceAsync(staffingResource.Id, onFail: _errorHandler.HandleError);
                yield return operation;                  

                if (operation.CompletedSuccessfully)
                {
                    // Rerun the search
                    SearchPane.Search();

                    if (ActiveStaffingResource != null && ActiveStaffingResource.Id == staffingResource.Id)
                        ActiveItem.TryClose();
                }
            }
        }

        public IEnumerable<IResult> Save()
        {
            using (ActiveDetail.Busy.GetTicket())
            {
                var operation = ActiveRepository.SaveAsync(onFail: _errorHandler.HandleError);
                yield return operation;

                if (operation.CompletedSuccessfully)
                {
                    SearchPane.Search(ActiveStaffingResource.Id);

                    NotifyOfPropertyChange(() => CanSave);
                    NotifyOfPropertyChange(() => CanCancel);
                }
            }
        }

        public void Cancel()
        {
            bool shouldClose = ActiveStaffingResource.EntityFacts.EntityState.IsAdded();
            ActiveRepository.RejectChanges();

            if (shouldClose)
                ActiveDetail.TryClose();
        }
    }
}