using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.IO;
using System.Runtime;
using System.Runtime.InteropServices;
using Steamworks;

namespace HoverWorkshopTool
{
    enum ListMode
    {
        LocalFiles,
        OnlineWorkshop
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        AppId_t app;
        ListMode state = ListMode.LocalFiles;

        public MainWindow()
        {
            InitializeComponent();
            app = new AppId_t(711030);//280180);
        }

        private void T_Tick(object sender, EventArgs e)
        {
            labelSteamStatus.Content = "Steam Link Status : " + (SteamAPI.IsSteamRunning() ? "available" : "not available");
            SteamAPI.RunCallbacks();

            if (pending != null)
            {
                pending();
                pending = null;
            }
        }

        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            SteamAPI.Init();

            DispatcherTimer t = new DispatcherTimer();
            t.Interval = new TimeSpan(0, 0, 1);
            t.Tick += T_Tick;
            t.Start();
        }

        private void listBox_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshMissionList();
        }

        private List<string> _Missions = new List<string>();
        private List<SteamUGCDetails_t> _UGCDetails = new List<SteamUGCDetails_t>();

        private void RefreshMissionList()
        {
            Guid localLowId = new Guid("A520A1A4-1780-4FF6-BD18-167343C5AF16");
            string DataPath = GetKnownFolderPath(localLowId);
            DataPath += "\\Fustygame\\Hover\\Missions";

            var dirList = Directory.GetDirectories(DataPath);

            foreach (var dir in dirList)
            {
                var enumFiles = Directory.GetFiles(dir);

                foreach (var f in enumFiles)
                    if (f.EndsWith(".xml"))
                    {
                        var nameStr = dir.Replace(DataPath + "\\", "") + "\\" + System.IO.Path.GetFileNameWithoutExtension(f);
                        _Missions.Add(nameStr);
                    }
            }

            listBox.ItemsSource = _Missions.ToArray();
        }

        private void RefreshWorkshopList()
        {
            UGCQueryHandle_t ugq = SteamUGC.CreateQueryUserUGCRequest(SteamUser.GetSteamID().GetAccountID(), EUserUGCList.k_EUserUGCList_Published, EUGCMatchingUGCType.k_EUGCMatchingUGCType_Items, EUserUGCListSortOrder.k_EUserUGCListSortOrder_CreationOrderDesc, app, app, 1); // TODO : Max 50 per page, list everything
            var steamCall = SteamUGC.SendQueryUGCRequest(ugq);
            p_queryCompleted = CallResult<SteamUGCQueryCompleted_t>.Create();
            p_queryCompleted.Set(steamCall, OnUGCQueryResult);
        }

        string GetKnownFolderPath(Guid knownFolderId)
        {
            IntPtr pszPath = IntPtr.Zero;
            try
            {
                int hr = SHGetKnownFolderPath(knownFolderId, 0, IntPtr.Zero, out pszPath);
                if (hr >= 0)
                    return Marshal.PtrToStringAuto(pszPath);
                throw Marshal.GetExceptionForHR(hr);
            }
            finally
            {
                if (pszPath != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(pszPath);
            }
        }

        [DllImport("shell32.dll")]
        static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr pszPath);

        private string SelectedFileName;

        private void listBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if(listBox.SelectedItem != null && (listBox.SelectedItem as string) != "")
                onContentSelectedGrid.IsEnabled = true;

            contentTitle.Text = "";
            contentDesc.Text = "";

            imagePath.Content = "No image selected";
            imageValid = false;

            SelectedFileName = (string)listBox.SelectedItem;

            if(state == ListMode.OnlineWorkshop && listBox.SelectedIndex != -1)
            {
                contentTitle.Text = _UGCDetails[listBox.SelectedIndex].m_rgchTitle;
                contentDesc.Text = _UGCDetails[listBox.SelectedIndex].m_rgchDescription;
            }
        }

        private void sendButton_Click(object sender, RoutedEventArgs e)
        {
            if (contentTitle.Text == null || contentTitle.Text == "")
            {
                MessageBox.Show("You must put a title to your content");
                return;
            }

            if (contentDesc.Text == null || contentDesc.Text == "")
            {
                MessageBox.Show("You must put a description to your content");
                return;
            }

            if(!imageValid)
            {
                MessageBox.Show("You must put an image for your content");
                return;
            }

            var result = SteamUGC.CreateItem(app, EWorkshopFileType.k_EWorkshopFileTypeCommunity);
            p_createdItem = CallResult<CreateItemResult_t>.Create();
            p_createdItem.Set(result, CreatedWorkshopItem);
        }

        private void OnUGCQueryResult(SteamUGCQueryCompleted_t result, bool ioFailure)
        {
            _UGCDetails.Clear();
            _Missions.Clear();

            for (int i = 0; i < result.m_unNumResultsReturned; i++)
            {
                SteamUGCDetails_t infos;
                SteamUGC.GetQueryUGCResult(result.m_handle, (uint)i, out infos);

                _Missions.Add(infos.m_rgchTitle + " - " + infos.m_nPublishedFileId);
                _UGCDetails.Add(infos);

                // Clear broken files
                if (infos.m_rgchTitle == null || infos.m_rgchTitle == "")
                    SteamUGC.DeleteItem(infos.m_nPublishedFileId);
            }

            listBox.ItemsSource = _Missions.ToArray();
        }

        private void SubmitItemCallResult(SubmitItemUpdateResult_t res, bool ioFailure)
        {
            runMT runMTCB = () =>
            {
                if (res.m_eResult == EResult.k_EResultOK)
                    MessageBox.Show("Successfully sent to Steam");
                else
                    MessageBox.Show("Failed upload to Steam");
            };

            pending = runMTCB;
        }

        private delegate void runMT();
        private runMT pending;

        private CallResult<SubmitItemUpdateResult_t> p_workshopUpd;
        private CallResult<SteamUGCQueryCompleted_t> p_queryCompleted;
        private CallResult<CreateItemResult_t> p_createdItem;

        private void CreatedWorkshopItem(CreateItemResult_t res, bool ioFailure)
        {
            runMT runMTCB = () =>
            {
                if (res.m_bUserNeedsToAcceptWorkshopLegalAgreement)
                {
                    MessageBox.Show("You must accept first the workshop legal agreement");
                    return;
                }

                // UGCUpdateHandle_t ugHandle = new UGCUpdateHandle_t(res.m_nPublishedFileId.m_PublishedFileId);

                UGCUpdateHandle_t ugHandle = SteamUGC.StartItemUpdate(app, res.m_nPublishedFileId);

                SteamUGC.SetItemTitle(ugHandle, contentTitle.Text);
                SteamUGC.SetItemDescription(ugHandle, contentDesc.Text);
                SteamUGC.SetItemPreview(ugHandle, (string)imagePath.Content);

                string runtimePath = AppDomain.CurrentDomain.BaseDirectory;

                if (!Directory.Exists(runtimePath + "Workshop"))
                    Directory.CreateDirectory(runtimePath + "Workshop");

                if (!Directory.Exists(runtimePath + "Workshop\\" + res.m_nPublishedFileId))
                    Directory.CreateDirectory(runtimePath + "Workshop\\" + res.m_nPublishedFileId);

                Guid localLowId = new Guid("A520A1A4-1780-4FF6-BD18-167343C5AF16");
                string DataPath = GetKnownFolderPath(localLowId);
                DataPath += "\\Fustygame\\Hover\\Missions";

                if (!Directory.Exists(System.IO.Path.GetDirectoryName(runtimePath + "Workshop\\" + res.m_nPublishedFileId + "\\" + SelectedFileName + ".xml")))
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(runtimePath + "Workshop\\" + res.m_nPublishedFileId + "\\" + SelectedFileName + ".xml"));

                File.Copy(DataPath + "\\" + SelectedFileName + ".xml", runtimePath + "Workshop\\" + res.m_nPublishedFileId + "\\" + SelectedFileName + ".xml");

                SteamUGC.SetItemContent(ugHandle, (runtimePath + "Workshop\\" + res.m_nPublishedFileId + "\\").Replace("\\", "/"));
                SteamUGC.SetItemTags(ugHandle, new List<string> { "Races" }); // Add language
                SteamUGC.SetItemVisibility(ugHandle, ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic);
                
                var steamCall = SteamUGC.SubmitItemUpdate(ugHandle, "");
                p_workshopUpd = CallResult<SubmitItemUpdateResult_t>.Create();
                p_workshopUpd.Set(steamCall, SubmitItemCallResult);

                MessageBox.Show("Submitting to Steam...");

                _Missions.Clear();
                //RefreshWorkshopList();
            };

            pending = runMTCB;
        }

        private void workshopOnlineButton_Click(object sender, RoutedEventArgs e)
        {
            state = ListMode.OnlineWorkshop;
            contentTitle.Text = "";
            contentDesc.Text = "";
            onContentSelectedGrid.IsEnabled = false;

            imagePath.Content = "No image selected";
            imageValid = false;

            buttonDelete.IsEnabled = true;

            listBox.ItemsSource = null;

            RefreshWorkshopList();
        }

        private void localButton_Click(object sender, RoutedEventArgs e)
        {
            state = ListMode.LocalFiles;
            contentTitle.Text = "";
            contentDesc.Text = "";
            onContentSelectedGrid.IsEnabled = false;

            listBox.ItemsSource = null;

            imagePath.Content = "No image selected";
            imageValid = false;

            buttonDelete.IsEnabled = false;

            _Missions.Clear();
            RefreshMissionList();
        }

        bool imageValid;

        private void button_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.ShowDialog();

            if (dlg.FileName.EndsWith(".jpg") || dlg.FileName.EndsWith(".png"))
            {
                System.IO.FileInfo inf = new FileInfo(dlg.FileName);

                if (inf.Length > 1048576)
                    MessageBox.Show("The image should not be above 1MB");
                else
                {
                    imagePath.Content = dlg.FileName;
                    imageValid = true;
                }
            }
            else
                MessageBox.Show("Invalid image format");
        }

        private void buttonDelete_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult res = MessageBox.Show("Do you really want to delete your workshop file?", "Confirmation", MessageBoxButton.YesNo);

            if (res == MessageBoxResult.Yes)
            {
                SteamUGC.DeleteItem(_UGCDetails[listBox.SelectedIndex].m_nPublishedFileId);
                MessageBox.Show("Workshop file deleted with success");
                workshopOnlineButton_Click(null, null);
            }
        }
    }
}
