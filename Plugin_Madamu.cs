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
using System.Collections.Specialized;
using LiveViewer;
using LiveViewer.Tool;
using LiveViewer.HtmlParser;

namespace Plugin_Madamu {
    public class Plugin_Madamu : ISitePlugin {
        #region ■定義

        private class FormFlashMadamu : FormFlash {
            public Timer  Timer   = new Timer(); //メッセージ定期チェック用
            public string Message = null;        //メッセージ(前回との比較用)

            public FormFlashMadamu(Performer pef)
                : base(pef) {
                FormClosed  += new FormClosedEventHandler(FormFlash_FormClosed);
                FlashLoaded += new FormFlash.FormFlashEventHandler(FormFlash_FlashLoaded);
                Timer.Tick  += new EventHandler(FormFlash_Timer_Tick);
            }

            private void FormFlash_FormClosed(object sender, FormClosedEventArgs e) {
                Timer.Dispose();
            }

            private void FormFlash_FlashLoaded(FormFlash ff) {
                Timer.Interval = 1000;
                Timer.Enabled  = true;
            }

            private void FormFlash_Timer_Tick(object sender, EventArgs e) {
                //メッセージ取得
                try {
                    string sMes = FlashGetVariable("chat_text.text");
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

        private WebClient Client          = new WebClient(); //HTML取得用
        private NameValueCollection Param = new NameValueCollection(); //HTML取得用
        private Parser Parser             = new Parser();    //HTML解析用パーサ

        private Regex RegexGetID     = new Regex("chat\\.php\\?hash\\=(.+)", RegexOptions.Compiled); //ID取得用
        private Regex RegexGetName   = new Regex("(.*)\\(([0-9]+)歳\\)$", RegexOptions.Compiled);
        private Regex RegexGetStatus = new Regex("status +([a-zA-Z0-9_]*) +([a-zA-Z0-9_]*) *$", RegexOptions.Compiled); //Status取得用
        private Regex RegexGetNew    = new Regex("new_icon +([a-zA-Z0-9_]+)", RegexOptions.Compiled);
        private Regex RegexGetImg    = new Regex("background-image:url\\(/([^\\)]*)\\)", RegexOptions.Compiled); //DSパフォIMG取得用
        private Regex RegexGetSwf    = new Regex("<param name=\"movie\" value=\"([^\"]*)\"", RegexOptions.Compiled);

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


        #region ■ISitePluginBaseインターフェースの実装

        public string Site       { get { return "Madamu"; } }

        public string Caption    { get { return "マダムとおしゃべり館用のプラグイン(2019/03/22版)"; } }

        public string TopPageUrl { get { return "http://www.madamu.tv/"; } }

        public void Begin() {
            //プラグイン開始時処理

            //コンパイルするための準備
            JScriptCodeProvider jcp = new JScriptCodeProvider();

            //コンパイルパラメータ（メモリ内で生成）
            string[]           assemblys = new string[] { Assembly.GetAssembly(this.GetType()).Location };
            CompilerParameters cp        = new CompilerParameters(assemblys);
            cp.GenerateInMemory = true;

            //コンパイル
            CompilerResults cres = jcp.CompileAssemblyFromSource(cp, JsSource);

            //コンパイルしたアセンブリを取得
            Assembly asm = cres.CompiledAssembly;

            //クラスのTypeを取得
            JsExecuterType = asm.GetType("JSExecuter.JSExecuter");

            //インスタンスの作成
            JsExecuterObject = Activator.CreateInstance(JsExecuterType);

            //POSTするデーター
            Param.Add("kbn","1"); 
            Param.Add("list","normal"); 
        }

        public void End() {
            //プラグイン終了時処理
        }

        public List<Performer> Update() {
            List<Performer> pefs = new List<Performer>();

            try {
                //WebにPOSTして、JSONデータを取得する
                Client.Headers.Add(HttpRequestHeader.UserAgent, Pub.UserAgent + "_" + Site);
                Client.Headers.Add(HttpRequestHeader.Referer, TopPageUrl + "top.php");
                Client.Headers.Add("X-Requested-With","XMLHttpRequest");
                Client.Headers.Add("Accept","application/json, text/javascript, */*; q=0.01");
                Client.Headers.Add("Accept-Language","ja,en-US;q=0.8,en;q=0.6");

                byte[] resArray = Client.UploadValues(TopPageUrl + "ajax/get_publisher_list.php", Param);
                string resData = "(" + Encoding.UTF8.GetString(resArray) + ");";
                //string resData = ("({\"result\":false,\"html\":\"abcdef\",\"banner_html\":\"\"});");

                //Jsファイルの内容を実行する
                JSObject top = JsExecuterType.InvokeMember("Eval", BindingFlags.InvokeMethod, null, JsExecuterObject, new object[] { resData.ToString() }) as JSObject;
                string sHtml = top.GetField("html", BindingFlags.Default).GetValue(null).ToString();
                Parser.LoadHtml(sHtml);
                Parser.ParseTree();

                List<HtmlItem> tagTops = Parser.Find("div", "class", "onlinebox");
                Parser.Clear();

                if (Pub.DebugMode == true ) Log.Add(Site, "tagTops.Count: " + tagTops.Count, LogColor.Warning); //DEBUG

                foreach (HtmlItem item in tagTops) {
                    string sID = RegexGetID.Match(item.Find("a", "class", "to_prof", true)[0].GetAttributeValue("href")).Groups[1].Value;
                    if (sID == "") continue;
                    if (Pub.DebugMode == true )
                        if (pefs.Count < 1) Log.Add(Site, "ID OK", LogColor.Warning); //DEBUG

                    Performer p = new Performer(this, sID);
                    p.Name = RegexGetName.Replace(item.Find("p", "class", "hundle", true)[0].Items[0].Text, "$1");
                    //p.ImageUrl = TopPageUrl + RegexGetImg.Match(item.Find("div", "style", true)[0].GetAttributeValue("style")).Groups[1].Value;
                    //p.ImageUpdateCheck = false;

                    string sStat = RegexGetStatus.Replace(item.Find("div", "class", RegexGetStatus, true)[0].GetAttributeValue("class"), "$1");
                    switch (sStat) {
                        case "online"  : p.Dona = false; p.TwoShot = false; break;
                        case "waiting" : p.Dona = false; p.TwoShot = false; p.OtherInfo = "待合せ中 "; break;
                        case "twoshot" : p.Dona = true;  p.TwoShot = true;  break;
                        case "premium" : p.Dona = true;  p.TwoShot = true;  p.RoomName  = "ﾌﾟﾚﾐｱﾑ"   ; break;
                        case "party"   : p.Dona = true;  p.TwoShot = false; break;
                        case "offline" : Log.Add(Site + " - " + p.Name, "offline", LogColor.Error); continue;
                        default: Log.Add(Site + "-ERROR", "不明な状態:" + sStat, LogColor.Error); break;
                    }

                    if (item.Find("div", "class", RegexGetNew, true).Count > 0) {
                        string sNew = RegexGetNew.Replace(item.Find("div", "class", RegexGetNew, true)[0].GetAttributeValue("class"), "$1");
                        if (sNew == "new01" || sNew == "new02") p.Debut = true;
                        else if (sNew == "new03" || sNew == "new04") p.NewFace = true;
                    }

                    if (item.Find("p", "class", "hundle", true).Count > 0) {
                        string sAge = RegexGetName.Replace(item.Find("p", "class", "hundle", true)[0].Items[0].Text, "$2");
                        if (sAge != "") p.Age = int.Parse(sAge);
                    }

                    /*
                    HtmlItem tmp1 = item.Find("span", "class", "title", true)[0];
                    if (tmp1.Items.Count > 0) {
                        p.OtherInfo += HttpUtilityEx.HtmlDecode(tmp1.Items[0].Text);
                    }
                    */

                    if (Pub.DebugMode == true )
                        if (pefs.Count < 1) Log.Add(Site, "pefs.Add OK", LogColor.Warning); //DEBUG

                    pefs.Add(p);
                }
                return pefs;
            } catch (WebException ex){
                //読み込み失敗(Web関連)
                Log.Add(Site + "-Update失敗(Web)", ex.ToString(), LogColor.Error);
                return null;
            } catch (Exception ex) {
                Log.Add(Site + "-Update失敗", ex.ToString(), LogColor.Error);
                return null;
            }
        }

        public FormFlash OpenFlash(Performer performer) {
            //フラッシュ窓を返す
            //return new FormFlashMadamu(performer);
            return null;
        }

        public string GetFlashUrl(Performer performer) {
            string sFlash = null;
/*            try {
                using (WebClient wc = new WebClient()) {
                    wc.Headers.Add(HttpRequestHeader.UserAgent, Pub.UserAgent + "_" + Site);
                    wc.Headers.Add("Accept-Language","ja,en-US;q=0.8,en;q=0.6");
                    string sHtml = wc.DownloadString(TopPageUrl + "chat.php?hash=" + performer.ID);
                    if (RegexGetSwf.Match(sHtml).Groups[1].Value != null) {
                        sFlash = TopPageUrl + RegexGetSwf.Match(sHtml).Groups[1].Value;
                    }
                    Pub.WebRequestCount++; //GUIの読込回数を増やす
                }
            } catch (Exception ex) {
                Log.Add(Site + "-GetFlashUrl失敗", ex.ToString(), LogColor.Error);
            }
*/
            return sFlash;
        }

        public Clipping GetFlashClipping(Performer performer) {
            //Flashの切り抜き方法を返す
            return null;
        }

        public string GetProfileUrl(Performer performer) {
            //プロフィールURLを返す
            return TopPageUrl + "chat.php?hash=" + performer.ID;
        }

        #endregion
    }
}
