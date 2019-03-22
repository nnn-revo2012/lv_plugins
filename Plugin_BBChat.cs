using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.CodeDom.Compiler;
using Microsoft.JScript;
using LiveViewer;
using LiveViewer.Tool;
using LiveViewer.HtmlParser;
using AxSHDocVw;
using AxShockwaveFlashObjects;

namespace Plugin_BBChat {
    public class Plugin_BBChat : ISitePlugin {
        #region ■定義

        private class FormFlashBBChat : FormFlash {
            private static Regex RegexGetText = new Regex("name=\"dispMsg\"(.*)<string>(.*)</string>", RegexOptions.Compiled);
            public string Message = null;        //メッセージ(前回との比較用)

            public FormFlashBBChat(Performer pef)
                : base(pef) {
                Flash.FlashCall += new _IShockwaveFlashEvents_FlashCallEventHandler(Flash_FlashCall);
            }

            private void Flash_FlashCall(object sender, _IShockwaveFlashEvents_FlashCallEvent e) {
                try {
                    string sMes =  HttpUtilityEx.HtmlDecode(RegexGetText.Match(e.request).Groups[2].Value); //タグを消す
                    sMes = Regex.Replace(sMes, "[\x00-\x1f]", "", RegexOptions.Compiled); //エラーになるコードを削除
                    if (sMes != null && sMes != "" && sMes != Message) {
                        Message = sMes;
                        Log.AddMessage(Performer, sMes); //メッセージログ表示
                    }
                } catch {
                }
            }

        }

        #endregion


        #region ■オブジェクト

        private Regex     RegexGetSwf    = new Regex("<param name=\"movie\" value=\"([^\"]*)\"", RegexOptions.Compiled);
        private WebClient Client         = new WebClient(); //HTML取得用

        private Type   JsExecuterType   = null;
        private object JsExecuterObject = null;
        private string JsSource         = @"
            package JSExecuter
            {
                public class JSExecuter {
                    public function Eval(sJsCode : String) : Object { 
                        return eval(sJsCode);
                    }
                }
            }
        ";

        #endregion


        #region ■ISitePluginインターフェースの実装

        public string Site       { get { return "bb-chat"; } }

        public string Caption    { get { return "BBChat用のプラグイン(2015/05/23版)"; } }

        public string TopPageUrl { get { return "http://bb-chat.tv/"; } }

        public void Begin() {
            //プラグイン開始時処理

            //コンパイルするための準備
            JScriptCodeProvider jcp = new JScriptCodeProvider();

            //コンパイルパラメータ（メモリ内で生成）
            string[] assemblys = new string[] { Assembly.GetAssembly(this.GetType()).Location};
            CompilerParameters cp = new CompilerParameters(assemblys);
            cp.GenerateInMemory = true;

            //コンパイル
            CompilerResults cres = jcp.CompileAssemblyFromSource(cp, JsSource);

            //コンパイルしたアセンブリを取得
            Assembly asm = cres.CompiledAssembly;

            //クラスのTypeを取得
            JsExecuterType = asm.GetType("JSExecuter.JSExecuter");

            //インスタンスの作成
            JsExecuterObject = Activator.CreateInstance(JsExecuterType);
        }

        public void End() {
            //プラグイン終了時処理
        }

        public List<Performer> Update() {
            List<Performer> pefs = new List<Performer>();

            try {
                //WebからJsファイルを読み取る
                Client.Headers.Add(HttpRequestHeader.UserAgent, Pub.UserAgent + "_" + Site); //User-Agentを設定
                StringBuilder sb = new StringBuilder();
                using (Stream s = Client.OpenRead(TopPageUrl + "ajax/public/get_online_girls.php")) {
                    using (StreamReader sr = new StreamReader(s, Encoding.GetEncoding("UTF-8"))) {
                        sb.AppendLine(sr.ReadToEnd());
                    }
                }
                string sJson = sb.ToString().Split('\n')[0] + ";";

                //Jsファイルの内容を実行する
                ArrayObject obj = JsExecuterType.InvokeMember("Eval", BindingFlags.InvokeMethod, null, JsExecuterObject, new object[] { sJson }) as ArrayObject;
                if (Pub.DebugMode == true ) Log.Add(Site, "obj.length: " + (int)obj.length, LogColor.Warning); //DEBUG
                for (int i = 0; i < (int)obj.length; i++) {
                    JSObject  jso = obj[i] as JSObject;
                    string    sID = jso.GetField("hash", BindingFlags.Default).GetValue(null) as string;
                    if (Pub.DebugMode == true )
                        if (pefs.Count < 1) Log.Add(Site, "ID OK", LogColor.Warning); //DEBUG
                    Performer p = new Performer(this, sID);
                    p.Name      = HttpUtilityEx.HtmlDecode((string)jso.GetField("nick_name", BindingFlags.Default).GetValue(null));
                    string sImg = jso.GetField("img", BindingFlags.Default).GetValue(null) as string;
                    p.ImageUrl = sImg.Replace("/bbchatgirl/", "/bbchatgirl_index/"); //2015/05/20
                    p.ImageUpdateCheck = false;

                    //オンライン状態
                    int iStatus = (int)jso.GetField("online", BindingFlags.Default).GetValue(null);
                    switch (iStatus) {
                        case 1: p.Dona = false; p.TwoShot = false;
                                if ((bool)jso.GetField("appointment", BindingFlags.Default).GetValue(null))
                                    p.OtherInfo = "待合せ中 ";
                                break;
                        case 2: p.Dona = true;  p.TwoShot = true; break;
                        default: continue; //offline
                    }

                    //年齢
                    int iAge;
                    p.Age = int.TryParse(jso.GetField("age", BindingFlags.Default).GetValue(null) as string, out iAge) ? iAge : 0;

                    //新人・デビュー・マイク
                    if ((bool)jso.GetField("today", BindingFlags.Default).GetValue(null)) {
                        p.Debut = true;
                    } else if ((bool)jso.GetField("new", BindingFlags.Default).GetValue(null)) {
                        p.NewFace = true;
                    }
                    p.Mic = (bool)jso.GetField("mike", BindingFlags.Default).GetValue(null);

                    if (Pub.DebugMode == true )
                        if (pefs.Count < 1) Log.Add(Site, "pefs.Add OK", LogColor.Warning); //DEBUG

                    pefs.Add(p);
                }
                return pefs;
            } catch (Exception ex) {
                Log.Add(Site + "-Update失敗", ex.ToString(), LogColor.Error);
                return null;
            }
        }

        public FormFlash OpenFlash(Performer performer) {
            //フラッシュ窓を返す
            return new FormFlashBBChat(performer);
        }
        
        public string GetFlashUrl(Performer performer) {
            //FlashのURLを返す・・待機画像ページのHTMLから取得する
            string sFlash = null;
            try {
                using (WebClient wc = new WebClient()) {
                    wc.Headers.Add(HttpRequestHeader.UserAgent, Pub.UserAgent + "_" + Site);
                    string sHtml = wc.DownloadString(TopPageUrl + "online.php?id=" + performer.ID);
                    sFlash = TopPageUrl + RegexGetSwf.Match(sHtml).Groups[1].Value;
                    Pub.WebRequestCount++; //GUIの読込回数を増やす
                }
            } catch (Exception ex) {
                Log.Add(Site + "-GetFlashUrl失敗", ex.ToString(), LogColor.Error);
            }
            return sFlash;
        }

        public Clipping GetFlashClipping(Performer performer) {
            //Flashの切り抜き方法を返す
            return null;
        }

        public string GetProfileUrl(Performer performer) {
            //プロフィールURLを返す
            //プロフィールだけの画面：return TopPageUrl + "girl-syosai.php?girl=" + performer.ID;
            return TopPageUrl + "online.php?s=1&id=" + performer.ID;
        }

        #endregion
    }
}
