using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Args;
using XrmToolBox.Extensibility.Interfaces;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using System.Collections.Generic;

namespace Data8.ActivityCascadeRules
{
    public partial class PluginControl : PluginControlBase, IGitHubPlugin, IPayPalPlugin, IHelpPlugin, IStatusBarMessenger
    {
        class RelationshipCell
        {
            public RelationshipCell(OneToManyRelationshipMetadata rel)
            {
                Relationship = rel;
                CascadeType = rel.CascadeConfiguration.Assign.Value;
            }

            public OneToManyRelationshipMetadata Relationship { get; }

            public CascadeType CascadeType { get; set; }

            public override string ToString()
            {
                return CascadeType.ToString();
            }
        }

        #region Base tool implementation

        public PluginControl()
        {
            InitializeComponent();
        }

        public event EventHandler<StatusBarMessageEventArgs> SendMessageToStatusBar;

        public void LoadRelationships()
        {
            tsbCancel.Enabled = true;

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading Relationships...",
                Work = (w, e) =>
                {
                    var request = new RetrieveAllEntitiesRequest
                    {
                        EntityFilters = EntityFilters.Entity | EntityFilters.Relationships
                    };

                    var response = (RetrieveAllEntitiesResponse)Service.Execute(request);

                    e.Result = response.EntityMetadata;
                },
                ProgressChanged = e =>
                {
                    if (SendMessageToStatusBar != null)
                        SendMessageToStatusBar(this, new StatusBarMessageEventArgs(e.ProgressPercentage, $"progress at {e.ProgressPercentage}%"));
                },
                PostWorkCallBack = e =>
                {
                    tsbCancel.Enabled = false;
                    if (!e.Cancelled)
                    {
                        var entities = (EntityMetadata[])e.Result;

                        // Get a list of all activity-type entities
                        var activityEntities = entities
                            .Where(entity => entity.IsActivity == true && entity.ManyToOneRelationships.Any(rel => rel.ReferencingAttribute == "regardingobjectid"))
                            .OrderBy(entity => entity.DisplayName.UserLocalizedLabel.Label)
                            .ToArray();
                        
                        // Get a list of all entities that can have activities
                        var partyEntities = entities
                            .Single(entity => entity.LogicalName == "activitypointer")
                            .ManyToOneRelationships
                            .Where(rel => rel.ReferencingAttribute == "regardingobjectid")
                            .Select(rel => entities.Single(entity => entity.LogicalName == rel.ReferencedEntity))
                            .OrderBy(entity => entity.DisplayName.UserLocalizedLabel.Label)
                            .ToArray();

                        // Show the relationships between the two sets of entities in the grid
                        table.RowCount = 0;
                        table.ColumnCount = 0;

                        table.RowCount = activityEntities.Length;
                        table.ColumnCount = partyEntities.Length;

                        for (var i = 0; i < activityEntities.Length; i++)
                        {
                            table.Rows[i].HeaderCell.Value = activityEntities[i].DisplayName.UserLocalizedLabel.Label;
                            table.Rows[i].Tag = activityEntities[i];
                        }

                        for (var i = 0; i < partyEntities.Length; i++)
                        {
                            table.Columns[i].HeaderText = partyEntities[i].DisplayName.UserLocalizedLabel.Label;
                            table.Columns[i].SortMode = DataGridViewColumnSortMode.NotSortable;
                            table.Columns[i].Tag = partyEntities[i];
                        }

                        for (var row = 0; row < activityEntities.Length; row++)
                        {
                            for (var col = 0; col < partyEntities.Length; col++)
                            {
                                var rel = activityEntities[row].ManyToOneRelationships.SingleOrDefault(r =>
                                    r.ReferencingAttribute == "regardingobjectid" &&
                                    r.ReferencedEntity == partyEntities[col].LogicalName);

                                if (rel != null)
                                    table.Rows[row].Cells[col].Value = new RelationshipCell(rel);
                                else
                                    table.Rows[row].Cells[col].Style.BackColor = table.BackgroundColor;
                            }
                        }

                        tsbChangeAll.Enabled = true;
                        tsbSave.Enabled = false;
                    }
                },
                AsyncArgument = null,
                IsCancelable = true,
                MessageWidth = 340,
                MessageHeight = 150
            });
        }

        private void tsbClose_Click(object sender, EventArgs e)
        {
            CloseTool();
        }

        public override void ClosingPlugin(PluginCloseInfo info)
        {
            if (tsbSave.Enabled && !info.Silent)
            {
                var result = MessageBox.Show("You have unsaved changes. Do you want to save them now?", "Unsaved Changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button3);

                switch (result)
                {
                    case DialogResult.Yes:
                        ExecuteMethod(SaveChanges);
                        break;

                    case DialogResult.No:
                        break;

                    case DialogResult.Cancel:
                        info.Cancel = true;
                        break;
                }

                info.Silent = true;
            }

            base.ClosingPlugin(info);
        }

        private void tsbLoad_Click(object sender, EventArgs e)
        {
            HideNotification();

            ExecuteMethod(LoadRelationships);
        }

        private void tsbCancel_Click(object sender, EventArgs e)
        {
            CancelWorker();
            tsbCancel.Enabled = false;
            MessageBox.Show("Cancelled");
        }

        #endregion Base tool implementation

        #region Github implementation

        public string RepositoryName
        {
            get { return "ActivityCascadeRules"; }
        }

        public string UserName
        {
            get { return "Data8"; }
        }

        #endregion Github implementation

        #region PayPal implementation

        public string DonationDescription
        {
            get { return "Donation for Activity Cascade Rules XrmToolbox Plugin"; }
        }

        public string EmailAccount
        {
            get { return "finance@data-8.co.uk"; }
        }

        #endregion PayPal implementation

        #region Help implementation

        public string HelpUrl
        {
            get { return "https://github.com/Data8/ActivityCascadeRules"; }
        }

        #endregion Help implementation

        private void PluginControl_Load(object sender, EventArgs e)
        {
            ExecuteMethod(LoadRelationships);
        }

        private void table_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            ToggleCascadeRules(e.ColumnIndex, null, null);
        }

        private void table_RowHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            ToggleCascadeRules(null, e.RowIndex, null);
        }

        private void ToggleCascadeRules(int? col, int? row, CascadeType? cascadeType)
        {
            if (cascadeType == null)
            {
                var currentType = CascadeType.NoCascade;

                for (var x = 0; x < table.ColumnCount; x++)
                {
                    if (col != null && col != x)
                        continue;

                    for (var y = 0; y < table.RowCount; y++)
                    {
                        if (row != null && row != y)
                            continue;

                        var rel = (RelationshipCell)table.Rows[y].Cells[x].Value;

                        if (rel == null || rel.Relationship.IsCustomizable.Value == false)
                            continue;

                        if (rel.CascadeType > currentType)
                            currentType = rel.CascadeType;
                    }
                }

                var nextType = currentType + 1;

                if (nextType == CascadeType.RemoveLink)
                    nextType = CascadeType.NoCascade;

                if (nextType == CascadeType.UserOwned && col != null && ((EntityMetadata)table.Columns[col.Value].Tag).OwnershipType == OwnershipTypes.OrganizationOwned)
                    nextType = CascadeType.NoCascade;

                cascadeType = nextType;
            }

            for (var x = 0; x < table.ColumnCount; x++)
            {
                if (col != null && col != x)
                    continue;

                var entityType = (EntityMetadata)table.Columns[x].Tag;

                for (var y = 0; y < table.RowCount; y++)
                {
                    if (row != null && row != y)
                        continue;

                    var rel = (RelationshipCell)table.Rows[y].Cells[x].Value;

                    if (rel == null)
                        continue;

                    if (cascadeType == CascadeType.UserOwned && entityType.OwnershipType == OwnershipTypes.OrganizationOwned)
                        continue;

                    rel.CascadeType = cascadeType.Value;
                }
            }

            table.Refresh();
            table_SelectionChanged(this, EventArgs.Empty);
            CheckForChanges();
        }

        private void CheckForChanges()
        {
            tsbSave.Enabled = false;

            for (var x = 0; x < table.ColumnCount; x++)
            {
                for (var y = 0; y < table.RowCount; y++)
                {
                    var rel = (RelationshipCell)table.Rows[y].Cells[x].Value;

                    if (rel == null)
                        continue;

                    if (rel.CascadeType != rel.Relationship.CascadeConfiguration.Assign)
                    {
                        tsbSave.Enabled = true;
                        return;
                    }
                }
            }
        }

        private void table_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.ColumnIndex == -1 || e.RowIndex == -1)
            {
                e.Paint(e.ClipBounds, e.PaintParts);
                e.Handled = true;
                return;
            }

            e.Paint(e.ClipBounds, e.PaintParts & ~DataGridViewPaintParts.ContentForeground);
            e.Handled = true;

            var rel = (RelationshipCell) e.Value;

            if (rel == null)
                return;

            if (rel.CascadeType == rel.Relationship.CascadeConfiguration.Assign)
            {
                DrawCell(e, rel, table.Font);
            }
            else
            {
                using (var font = new Font(table.Font, FontStyle.Bold))
                {
                    DrawCell(e, rel, font);
                }
            }
        }

        private void DrawCell(DataGridViewCellPaintingEventArgs e, RelationshipCell rel, Font font)
        {
            var size = e.Graphics.MeasureString("Yy", font);
            var imgPos = new Point(e.CellBounds.Left, e.CellBounds.Top + (e.CellBounds.Height - 20) / 2);
            var textPos = new Point(e.CellBounds.Left + 20, (int) (e.CellBounds.Top + (e.CellBounds.Height - size.Height) / 2));
            var text = rel.CascadeType.ToString();
            var img = imageList.Images[(int)rel.CascadeType];
            e.Graphics.DrawImage(img, imgPos);

            var brush = rel.Relationship.IsCustomizable.Value ? SystemBrushes.ControlText : SystemBrushes.GrayText;
            e.Graphics.DrawString(text, font, brush, textPos);
        }

        private void table_SelectionChanged(object sender, EventArgs e)
        {
            var selection = table.SelectedCells.Cast<DataGridViewCell>().FirstOrDefault();

            if (selection == null)
            {
                descriptionLabel.Visible = false;
                return;
            }

            var relationship = (RelationshipCell)selection.Value;
            if (relationship == null)
            {
                descriptionLabel.Visible = false;
                return;
            }

            var entityType = (EntityMetadata)selection.OwningColumn.Tag;
            var entityName = selection.OwningColumn.HeaderText;
            var activityType = (EntityMetadata)selection.OwningRow.Tag;
            var activityNames = activityType.DisplayCollectionName.UserLocalizedLabel.Label;

            switch (relationship.CascadeType)
            {
                case CascadeType.NoCascade:
                    descriptionLabel.Text = $"When a {entityName} is reassigned, none of the related {activityNames} will be reassigned.";
                    break;

                case CascadeType.Cascade:
                    descriptionLabel.Text = $"When a {entityName} is reassigned, all of the related {activityNames} will be reassigned to the same user.";
                    break;

                case CascadeType.Active:
                    descriptionLabel.Text = $"When a {entityName} is reassigned, any active {activityNames} will be reassigned to the same user. Any closed {activityNames} will be left unchanged.";
                    break;

                case CascadeType.UserOwned:
                    descriptionLabel.Text = $"When a {entityName} is reassigned, any {activityNames} currently owned by the same user will be reassigned. Any {activityNames} currently assigned to a different user will be left unchanged.";
                    break;
            }

            if (!relationship.Relationship.IsCustomizable.Value)
                descriptionLabel.Text += "\r\nThis relationship cannot be customized";

            if (entityType.OwnershipType == OwnershipTypes.OrganizationOwned)
                descriptionLabel.Text += $"\r\nAs the {entityName} is Organization-owned, this relationship cannot be set to the UserOwned cascading type";

            descriptionLabel.Text += $"\r\n\r\nEntity Type: {entityType.LogicalName}\r\nActivity Type: {activityType.LogicalName}";

            descriptionLabel.Visible = true;
        }

        private void noCascadeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ToggleCascadeRules(null, null, CascadeType.NoCascade);
        }

        private void cascadeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ToggleCascadeRules(null, null, CascadeType.Cascade);
        }

        private void activeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ToggleCascadeRules(null, null, CascadeType.Active);
        }

        private void userOwnedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ToggleCascadeRules(null, null, CascadeType.UserOwned);
        }

        private void table_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            ToggleCascadeRules(e.ColumnIndex, e.RowIndex, null);
        }

        private void tsbSave_Click(object sender, EventArgs e)
        {
            ExecuteMethod(SaveChanges);
        }

        private void SaveChanges()
        {
            tsbCancel.Enabled = true;

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Saving Changes...",
                Work = (w, e) =>
                {
                    w.WorkerReportsProgress = true;
                    w.ReportProgress(0);
                    var updates = new List<OneToManyRelationshipMetadata>();

                    for (var x = 0; x < table.ColumnCount; x++)
                    {
                        for (var y = 0; y < table.RowCount; y++)
                        {
                            var rel = (RelationshipCell)table.Rows[y].Cells[x].Value;

                            if (rel == null)
                                continue;

                            if (rel.CascadeType == rel.Relationship.CascadeConfiguration.Assign)
                                continue;

                            var updatedRelationship = new OneToManyRelationshipMetadata
                            {
                                MetadataId = rel.Relationship.MetadataId,
                                SchemaName = rel.Relationship.SchemaName,
                                CascadeConfiguration = new CascadeConfiguration
                                {
                                    Assign = rel.CascadeType
                                }
                            };

                            updates.Add(updatedRelationship);
                        }
                    }

                    for (var i = 0; i < updates.Count && !e.Cancel; i++)
                    {
                        Service.Execute(new UpdateRelationshipRequest
                        {
                            Relationship = updates[i]
                        });

                        w.ReportProgress((i + 1) * 100 / updates.Count);
                    }
                },
                ProgressChanged = e =>
                {
                    if (SendMessageToStatusBar != null)
                        SendMessageToStatusBar(this, new StatusBarMessageEventArgs(e.ProgressPercentage, $"{e.ProgressPercentage}% updating relationships..."));
                },
                PostWorkCallBack = e =>
                {
                    tsbCancel.Enabled = false;
                    SendMessageToStatusBar(this, new StatusBarMessageEventArgs((int?)null));
                    ExecuteMethod(LoadRelationships);
                },
                AsyncArgument = null,
                IsCancelable = true,
                MessageWidth = 340,
                MessageHeight = 150
            });
        }
    }
}