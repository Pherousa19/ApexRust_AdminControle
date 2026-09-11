using Oxide.Core;
using System;
using UnityEngine;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine.Networking;

namespace Oxide.Plugins
{
    [Info("ChaosExtensionDownloader", "k1lly0u", "1.0.50")]
    class ChaosExtensionDownloader : RustPlugin
    {
        private void OnServerInitialized()
        {
            Debug.Log("ChaosExtensionDownloader.cs is just a downloader plugin and can be deleted after the Chaos extension has been installed");
            
            if (Interface.Oxide.GetExtension("Chaos") == null)
                ServerMgr.Instance.StartCoroutine(DownloadAndSave(true));
        }
        
        private IEnumerator DownloadAndSave(bool loadInstantly)
        {
            Debug.Log($"[Chaos] - Downloading latest version of Chaos...");

            string targetPath = Path.Combine(Interface.Oxide.ExtensionDirectory, "Oxide.Ext.Chaos.dll");
            string tempPath = Path.Combine(Interface.Oxide.ExtensionDirectory, "Oxide.Ext.Chaos.dll.tmp");
            string backupPath = Path.Combine(Interface.Oxide.ExtensionDirectory, "Oxide.Ext.Chaos.dll.backup");
            
            UnityWebRequest www = UnityWebRequest.Get("https://oxide.chaoscode.io/Oxide.Ext.Chaos.dll");
            www.SetRequestHeader("User-Agent", "Oxide.Ext.Chaos/1.0");
            www.SetRequestHeader("Accept", "application/octet-stream, */*");
            www.SetRequestHeader("Cache-Control", "no-cache");
            
            www.downloadHandler = new DownloadHandlerFile(loadInstantly ? targetPath : tempPath);

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[Chaos] - Failed to connect to file server : {www.result} {www.error}");
                www.Dispose();
                yield break;
            }

            while (!www.downloadHandler.isDone)
                yield return null;

            if (loadInstantly)
            {
                Debug.Log($"[Chaos] - Download completed! Loading extension...");
                Interface.Oxide.LoadExtension("Oxide.Ext.Chaos");
                
                Oxide.Core.Extensions.Extension extension = Interface.Oxide.GetExtension("Chaos");
                MethodInfo methodInfo = extension.GetType().GetMethod("LoadCorePlugins", BindingFlags.Public | BindingFlags.Instance);
                methodInfo.Invoke(extension, null);
                
                timer.In(1, ()=> Interface.Oxide.LoadAllPlugins());
            }
            else
            {
                if (!File.Exists(tempPath))
                {
                    Debug.LogError("[Chaos] - Downloaded file does not exist");
                    yield break;
                }

                FileInfo tempFileInfo = new FileInfo(tempPath);
                if (tempFileInfo.Length == 0)
                {
                    Debug.LogError("[Chaos] - Downloaded file is empty");
                    File.Delete(tempPath);
                    yield break;
                }

                Debug.Log("[Chaos] - Download completed, updating extension...");

                if (WriteFileToDisk(targetPath, tempPath, backupPath))
                    Debug.Log($"[Chaos] - Download completed! Restart your server for file to load");
            }
        }
        
        private bool WriteFileToDisk(string targetPath, string tempPath, string backupPath)
        {
            bool success = false;
            try
            {
                if (File.Exists(targetPath))
                {
                    if (File.Exists(backupPath))
                        File.Delete(backupPath);
                    File.Move(targetPath, backupPath);
                }

                File.Move(tempPath, targetPath);
                success = true;

                if (File.Exists(backupPath))
                    File.Delete(backupPath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Chaos] - Failed to replace file: {ex.Message}");

                if (!success && File.Exists(backupPath))
                {
                    try
                    {
                        if (File.Exists(targetPath))
                            File.Delete(targetPath);
                        File.Move(backupPath, targetPath);
                    }
                    catch (Exception restoreEx)
                    {
                        Debug.LogError($"[Chaos] - Failed to restore backup: {restoreEx.Message}");
                    }
                }

                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            
            return success;
        }
        
        [ConsoleCommand("chaos.forcedownload")]
        private void ccmdForceDownload(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && arg.Connection.authLevel != 2)
            {
                SendReply(arg, "You do not have the required auth level to use this command");
                return;
            }

            ServerMgr.Instance.StartCoroutine(DownloadAndSave(false));   
        }     
    }       
}     
    