#define SHOW_MOBILE //★この行を削除すればマシェリモバイルのパフォは表示されません

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

namespace Plugin_Macherie {
    public class Plugin_Macherie : ISitePlugin {
        #region ■定義

        private class FormFlashMacherie : FormFlash {
            public         Timer  Timer        = new Timer(); //メッセージ定期チェック用
            public         string Message      = null;        //メッセージ(前回との比較用)

            public FormFlashMacherie(Performer pef)
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
                    string sMes = FlashGetVariable("main_mc.girl_msg1.text");
                    sMes = Regex.Replace(sMes, "[\x00-\x1f]", "", RegexOptions.Compiled); //エラーになるコードを削除
                    if (sMes != null && sMes != "" && sMes != Message) {
                        Message = sMes;
                        //Log.AddMessage(Performer, sMes); //メッセージログ表示
                        Log.Add(Performer.Plugin.Site + " - " + Performer.Name, "≫" + sMes, LogColor.Pef_Message);
                    }
                } catch {
                }
            }
        }

        #endregion


        #region ■オブジェクト

        private WebClient Client       = new WebClient(); //HTML取得用

        private string[] xxNode = new string[]{"eventNode", "partyNode", "firstNode", "secondNode"};

        //HTML解析用の正規表現
        private Regex RegexGetSwf = new Regex("<param name=\"movie\" value=\"([^\"]*)\"", RegexOptions.Compiled);

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

        public string Site       { get { return "macherie"; } }

        public string Caption    { get { return "マシェリ用のプラグイン(2019/05/03版)"; } }

        public string TopPageUrl { get { return "https://macherie.tv/"; } }

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
            List<JSObject> jso2 = new List<JSObject>();

            try {
                //WebからJsファイルを読み取る
                StringBuilder sb = new StringBuilder();
                sb.Append("(");
                Client.Headers.Add(HttpRequestHeader.UserAgent, Pub.UserAgent + "_" + Site); //User-Agentを設定
                using (Stream       s  = Client.OpenRead("http://macherie.tv/xxnode.php"))
                using (StreamReader sr = new StreamReader(s, Encoding.GetEncoding("Shift_JIS"))) {
                    sb.Append(sr.ReadToEnd());
                }
                sb.Append(");");
                sb.Replace("\r\n", "");

                //Jsファイルの内容を実行する
                JSObject top = JsExecuterType.InvokeMember("Eval", BindingFlags.InvokeMethod, null, JsExecuterObject, new object[] { sb.ToString() }) as JSObject;

                //ノード毎にデーターを読み込む
                foreach (string sNode in xxNode) {
                    if (top.GetField(sNode, BindingFlags.Default).GetValue(null) as string == "notNode") continue;
                    ArrayObject obj = top.GetField(sNode, BindingFlags.Default).GetValue(null) as ArrayObject;
                    for (int i = 0; i < (int)obj.length; i++) {
                        JSObject jso = new JSObject();
                        jso = (JSObject)obj[i];
                        jso.AddField("node");
                        jso.SetMemberValue2("node", sNode); //データーにノード名を追加
                        jso2.Add(jso);
                    }
                }

            } catch (Exception ex) {
                Log.Add(Site + "-Update失敗", ex.ToString(), LogColor.Error);
                return null;
            }

            //データーを読み込んで pefs に追加
            foreach (JSObject jso in jso2) {
                string sID = jso.GetField("hs", BindingFlags.Default).GetValue(null) as string;
                Performer p = new Performer(this, sID);
                p.Name = jso.GetField("cn", BindingFlags.Default).GetValue(null) as string;

                //ステータスの取得
                string sStatus = jso.GetField("st", BindingFlags.Default).GetValue(null) as string;
                switch (sStatus) {
                    case "on2h": p.Dona = false; p.TwoShot = true; p.RoomName = "2ｼｮｯﾄ"; break;
                    case "2h": p.Dona = true; p.TwoShot = true; p.RoomName = "2ｼｮｯﾄ"; break;
                    case "onph": p.Dona = false; p.TwoShot = false; p.RoomName = "ﾊﾟｰﾃｨｰ"; break;
                    case "ph": p.Dona = true; p.TwoShot = false; p.RoomName = "ﾊﾟｰﾃｨｰ"; break;
#if SHOW_MOBILE
                    //case "mbh"  : p.Dona = true;  p.TwoShot = true;  p.RoomName = "ﾓﾊﾞｲﾙ";  p.Mobile = true; break;
#else
                    //case "mbh"  : continue; //モバイルなので登録しない
#endif
                    //case "cmh"  : continue; //CM(?)なので登録しない
                    case "wh": continue; //ワールドマシェリなので登録しない
                    case "offh": continue; //オフラインなので登録しない
                    default: Log.Add(Site + "-不明な状態:", sStatus, LogColor.Error); break;
                }

                //部屋の取得
                string sRoom = jso.GetField("node", BindingFlags.Default).GetValue(null) as string;
                switch (sRoom) {
                    case "eventNode":
                        p.RoomName = "ｲﾍﾞﾝﾄ"; break;
                    default: break;
                }

                //マイクの取得
                string sVo = jso.GetField("vo", BindingFlags.Default).GetValue(null) as string;
                if (int.Parse(sVo) == 1) p.Mic = true;

                //各種状態の取得
                string sCf = jso.GetField("cf", BindingFlags.Default).GetValue(null) as string;
                switch (int.Parse(sCf)) {
                    case 1: p.NewFace = true; break;                                   //新人
                    case 2: p.Debut = true; break;                                     //デビュー
                    case 3: p.OtherInfo += "高画質 "; break;                           //高画質
                    case 4: p.NewFace = true; p.OtherInfo += "高画質 "; break;         //新人+高画質
                    case 5: p.Debut = true; p.OtherInfo += "高画質 "; break;           //デビュー+高画質
                    case 8: p.OtherInfo += "CHECK "; break;                            //チェック
                    case 7: p.OtherInfo += "CHECK "; p.OtherInfo += "高画質 "; break;  //チェック+高画質
                    default: break;
                }

                //画像URLの取得
                string sCs = jso.GetField("cs", BindingFlags.Default).GetValue(null) as string;
                if (sCs != "cm") {
                    p.ImageUrl = "http://p.macherie.tv/imgs/op/180x135/"; // 2014/08/20大きさ修正
                } else {
                    p.ImageUrl = "http://p.macherie.tv/imgs/cm/180x135/"; // 2014/08/20大きさ修正
                }
                p.ImageUrl += jso.GetField("ph", BindingFlags.Default).GetValue(null) as string;
                p.ImageUpdateCheck = false;

                pefs.Add(p);
            }

            return pefs;

        }

        public FormFlash OpenFlash(Performer performer) {
            //フラッシュ窓を返す
            if (performer.RoomName == "ﾓﾊﾞｲﾙ") {
                MessageBox.Show("モバイルなので待機映像はありません");
                return null;
            } else {
                return new FormFlashMacherie(performer);
            }
        }

        public string GetFlashUrl(Performer performer) {
            //FlashのURLを返す・・待機画像ページのHTMLから取得する
            string sFlash = null;
            try {
                using (WebClient wc = new WebClient()) {
                    wc.Headers.Add(HttpRequestHeader.UserAgent, Pub.UserAgent + "_" + Site);
                    string sHtml = wc.DownloadString("http://macherie.tv/chat/shicho.php?id=" + performer.ID);
                    sFlash = "http://macherie.tv/" + RegexGetSwf.Match(sHtml).Groups[1].Value;
                    Pub.WebRequestCount++; //GUIの読込回数を増やす
                }
            } catch (Exception ex) {
                Log.Add(Site + "-GetFlashUrl失敗", ex.ToString(), LogColor.Error);
            }
            return sFlash;
        }

        public Clipping GetFlashClipping(Performer performer) {
            //Flashの切り抜き方法を返す
            Clipping c = new Clipping();
            c.OriginalSize.Width  = 792;  //フラッシュ全体の幅
            c.OriginalSize.Height = 506;  //フラッシュ全体の高さ
            c.ClippingRect.X      = 12;   //切り抜く領域の左上座標(X)
            c.ClippingRect.Y      = 49;   //切り抜く領域の左上座標(Y)
            c.ClippingRect.Width  = 596;  //切り抜く領域の幅
            c.ClippingRect.Height = 447;  //切り抜く領域の高さ
            c.Fixed               = true; //Flashが固定サイズ
            return c;
        }

        public string GetProfileUrl(Performer performer) {
            //プロフィールURLを返す
            //プロフィールだけの画面：return TopPageUrl + "profile/profile.php?sid=" + performer.ID;
            return TopPageUrl + "chat/shicho.php?id=" + performer.ID;
        }

        #endregion
    }
}
