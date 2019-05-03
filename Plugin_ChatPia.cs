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

namespace Plugin_ChatPia {
    public class Plugin_ChatPia : ISitePlugin {
        #region ■定義

        private class FormFlashChatPia : FormFlash {
            private static Regex  RegexGetText = new Regex("<B>([^<]*)</B>", RegexOptions.Compiled);
            public         Timer  Timer        = new Timer(); //メッセージ定期チェック用
            public         string Message      = null;        //メッセージ(前回との比較用)

            public FormFlashChatPia(Performer pef)
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
                    string sMes = FlashGetVariable("_root.infoMovie.chat.htmlText");
                    if (sMes != null && sMes != "" && sMes != Message) {
                        Message = sMes;

                        //タグを消してからメッセージログ登録
                        string sText = RegexGetText.Match(sMes).Groups[1].Value;
                        //Log.AddMessage(Performer, sText); //メッセージログ表示
                        Log.Add(Performer.Plugin.Site + " - " + Performer.Name, "≫" + sText, LogColor.Pef_Message);
                    }
                } catch {
                }
            }
        }

        #endregion


        #region ■オブジェクト

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

        public string Site       { get { return "ChatPia"; } }

        public string Caption    { get { return "CHATPIA用のプラグイン(2019/05/03版)"; } }

        public string TopPageUrl { get { return "https://www.chatpia.jp/"; } }

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
                StringBuilder sb = new StringBuilder();
                sb.Append("(");
                using (WebClient wc = new WebClient()) {
                    wc.Headers.Add(HttpRequestHeader.UserAgent, Pub.UserAgent + "_" + Site); //User-Agentを設定
                    using (Stream s = wc.OpenRead("https://www.chatpia.jp/lib/=/pia_load_online.js"))
                    using (StreamReader sr = new StreamReader(s, Encoding.GetEncoding("EUC-JP"))) {
                        sb.Append(sr.ReadToEnd());
                    }
                }
                sb.Append(");");
                sb.Replace("\r\n", "");
                //サーバーメンテ処理＆データー取得エラー処理
                if (Regex.IsMatch(sb.ToString(), "^\\(req='Maintenance") ||
                    Regex.IsMatch(sb.ToString(), "^\\(req='<!DOCTYPE")) {
                    throw new WebException("システムメンテナンス中です");
                } else if (!Regex.IsMatch(sb.ToString(), "';complete\\(req\\);\\);$")) {
                    throw new WebException("Webからデーターを取得できませんでした");
                }
                sb.Replace("req='", "");
                sb.Replace("';complete(req);", "");

                //Jsファイルの内容を実行する
                JSObject    top = JsExecuterType.InvokeMember("Eval", BindingFlags.InvokeMethod, null, JsExecuterObject, new object[] { sb.ToString() }) as JSObject;
                ArrayObject obj = top.GetField("performers", BindingFlags.Default).GetValue(null) as ArrayObject;

                if (Pub.DebugMode == true ) Log.Add(Site, "obj.length: " + obj.length, LogColor.Warning); //DEBUG
                for (int i = 0; i < (int)obj.length; i++) {
                    JSObject  jso = obj[i] as JSObject;
                    string    sID = jso.GetField("cod", BindingFlags.Default).GetValue(null).ToString();
                    if (Pub.DebugMode == true )
                        if (pefs.Count < 1) Log.Add(Site, "ID OK", LogColor.Warning); //DEBUG
                    Performer p = new Performer(this, sID);
                    p.Name      = HttpUtilityEx.HtmlDecode(jso.GetField("nam", BindingFlags.Default).GetValue(null) as string);
                    p.ImageUrl  = "http://picture.chatpia.jp/images/p2-" + sID;
                    p.ImageUpdateCheck = false;

                    int iStatus = (int)jso.GetField("sts", BindingFlags.Default).GetValue(null);
                    switch (iStatus) {
                        case 0: Log.Add(Site + " - " + p.Name, "offline", LogColor.Error); continue;
                        case 1: p.Dona = false; p.TwoShot = false;
                            string sOpt15 = jso.GetField("opf", BindingFlags.Default).GetValue(null) as string;
                            if (sOpt15.IndexOf("98") != -1) {
                                iStatus = 4; p.OtherInfo = "待合せ中 ";
                            }
                            break;
                        case 2: p.Dona = true;  p.TwoShot = false; break;
                        case 3: p.Dona = true;  p.TwoShot = true;  break;
                        case 4: p.Dona = false; p.TwoShot = false; p.OtherInfo = "待合せ中 "; break;
                        default: Log.Add(Site + "-ERROR", "不明な状態:" + iStatus, LogColor.Error); break;
                    }
                    p.Age = (int)jso.GetField("age", BindingFlags.Default).GetValue(null);
                    string sOption = jso.GetField("op1", BindingFlags.Default).GetValue(null) as string;
                    if (sOption == "99") continue;
                    switch (sOption) {
                        case "7": p.RoomName = "ｲﾍﾞﾝﾄ"; break;
                        case "9": p.RoomName = "ｲﾍﾞﾝﾄ"; break;
                        case "10": p.RoomName = "ｲﾍﾞﾝﾄ"; break;
                        case "11": p.RoomName = "ｲﾍﾞﾝﾄ"; break;
                        case "12": p.RoomName = "ｲﾍﾞﾝﾄ"; break;
                        case "99999": p.OtherInfo = "[管理]"; break;
                        //default:   p.OtherInfo = "[？：" + sOption + "]"; break;
                        default: break;
                    }
                    if (Pub.DebugMode == true)
                        if (sOption != "2" && sOption != "13") Log.Add(Site + " - " + p.Name, "op1: " + sOption, LogColor.Warning); //DEBUG

                    //color01: 超新人
                    string sOpt9  = jso.GetField("op9", BindingFlags.Default).GetValue(null) as string;
                    //03: 新人
                    string sOpt12 = jso.GetField("opc", BindingFlags.Default).GetValue(null) as string;

                    if (sOpt9 == "color01") {
                        p.Debut = true;
                    } else if (sOpt12.IndexOf("03") != -1) {
                        p.NewFace = true;
                    }

                    p.DonaCount = (int)jso.GetField("cnt", BindingFlags.Default).GetValue(null);
#if !NOCOMMENT
                    p.OtherInfo += HttpUtilityEx.HtmlDecode(jso.GetField("cha", BindingFlags.Default).GetValue(null) as string);
#endif
                    if (Pub.DebugMode == true )
                        if (pefs.Count < 1) Log.Add(Site, "pefs.Add OK", LogColor.Warning); //DEBUG

                    pefs.Add(p);
                }
                return pefs;
            } catch (WebException ex){
                //読み込み失敗(Web関連)
                Log.Add(Site + "-Update失敗(Web)", ex.ToString(), LogColor.Error);
                return pefs;
            } catch (Exception ex) {
                Log.Add(Site + "-Update失敗", ex.ToString(), LogColor.Error);
                return null;
            }
        }

        public FormFlash OpenFlash(Performer performer) {
            //フラッシュ窓を返す
            return new FormFlashChatPia(performer);
        }
        
        public string GetFlashUrl(Performer performer) {
            //FlashのURLを返す・・
            //return "http://ap.chatpia.jp/flash/peep.swf?ownerCode=19648&performerCode=" + performer.ID;
            return "https://assets.chatpia.jp/common/swf/chat/male_wait_cp.swf?ownerCode=19648&performerCode=" + performer.ID;
        }

        public Clipping GetFlashClipping(Performer performer) {
            //Flashの切り抜き方法を返す
            //return null;
            Clipping c = new Clipping();
            c.OriginalSize.Width  = 888;  //フラッシュ全体の幅
            c.OriginalSize.Height = 414;  //フラッシュ全体の高さ
            c.ClippingRect.X      = 60;   //切り抜く領域の左上座標(X)
            c.ClippingRect.Y      = 54;   //切り抜く領域の左上座標(Y)
            c.ClippingRect.Width  = 480;  //切り抜く領域の幅
            c.ClippingRect.Height = 360;  //切り抜く領域の高さ
            c.Fixed               = true; //Flashが固定サイズ
            return c;
        }

        public string GetProfileUrl(Performer performer) {
            //プロフィールURLを返す
            return TopPageUrl + "cr/p" + performer.ID; //2014/09/27修正
        }

        #endregion
    }
}
