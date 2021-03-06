﻿using System;
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
//using System.Net.Security;
//using System.Security.Cryptography.X509Certificates;
using System.Collections.Specialized;
using System.Globalization;
using LiveViewer;
using LiveViewer.Tool;
using LiveViewer.HtmlParser;

namespace Plugin_AngelLive {
    public class Plugin_AngelLive : ISitePlugin {
        #region ■定義

        private class FormFlashAngelLive : FormFlash {
            private static Regex  RegexGetText = new Regex("<B>([^<]*)</B>", RegexOptions.Compiled);
            public         Timer  Timer        = new Timer(); //メッセージ定期チェック用
            public         string Message      = null;        //メッセージ(前回との比較用)

            public FormFlashAngelLive(Performer pef)
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

        //サロペートペア＆結合文字 検出＆文字除去
        //\ud83d\ude0a
        //か\u3099
        private class HttpUtilityEx2 {
            public static string HtmlDecode(string s) {
                if (!IsSurrogatePair(s)) return HttpUtilityEx.HtmlDecode(s);

                StringBuilder sb = new StringBuilder();
                TextElementEnumerator tee = StringInfo.GetTextElementEnumerator(s);
                tee.Reset();
                while (tee.MoveNext()) {
                    string te = tee.GetTextElement();
                    if (1 < te.Length) continue; //サロペートペアまたは結合文字
                    sb.Append(te);
                }
                return HttpUtilityEx.HtmlDecode(sb.ToString());
            }

            public static bool IsSurrogatePair(string s) {
                StringInfo si = new StringInfo(s);
                return si.LengthInTextElements < s.Length;
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

        public string Site       { get { return "AngelLive"; } }

        public string Caption    { get { return "AngelLive用のプラグイン(2019/11/06版)"; } }

        public string TopPageUrl { get { return "https://www.angel-live.com/"; } }

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
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | (SecurityProtocolType)0x00000C00 | (SecurityProtocolType)0x00000300;
                using (WebClient wc = new WebClient()) {
                    wc.Headers.Add(HttpRequestHeader.UserAgent, Pub.UserAgent + "_" + Site); //User-Agentを設定
                    wc.Encoding = Encoding.GetEncoding("EUC-JP");
                    sb.Append(wc.DownloadString("https://livechat.angel-live.com/lib/=/load_online.js"));
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
                JSObject top = JsExecuterType.InvokeMember("Eval", BindingFlags.InvokeMethod, null, JsExecuterObject, new object[] { sb.ToString() }) as JSObject;
                ArrayObject obj = top.GetField("performers", BindingFlags.Default).GetValue(null) as ArrayObject;

                if (Pub.DebugMode == true ) Log.Add(Site, "obj.length: " + obj.length, LogColor.Warning); //DEBUG
                for (int i = 0; i < (int)obj.length; i++) {
                    JSObject  jso = obj[i] as JSObject;
                    string    sID = jso.GetField("cod", BindingFlags.Default).GetValue(null).ToString();
                    if (Pub.DebugMode == true )
                        if (pefs.Count < 1) Log.Add(Site, "ID OK", LogColor.Warning); //DEBUG
                    Performer p = new Performer(this, sID);
                    p.Name      = HttpUtilityEx2.HtmlDecode(jso.GetField("nam", BindingFlags.Default).GetValue(null) as string);
                    p.ImageUrl  = "http://picture.angel-live.com/images/p2-" + sID;
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

                    string sOption = jso.GetField("op1", BindingFlags.Default).GetValue(null) as string;
                    int iOp1;
                    if (int.TryParse(sOption, out iOp1)) {
                        if (iOp1 == 99)  p.OtherInfo = "[管理]";
                        else if (iOp1 == 97) p.RoomName = "ｶｯﾌﾟﾙ";
                        else if (iOp1 >= 6) p.RoomName = "ｲﾍﾞﾝﾄ";
                    }
                    if (Pub.DebugMode == true)
                        if (sOption != "2") Log.Add(Site + " - " + p.Name, "op1: " + sOption, LogColor.Warning); //DEBUG

                    //color01: 超新人
                    string sOpt9  = jso.GetField("op9", BindingFlags.Default).GetValue(null) as string;
                    //03: 新人
                    string sOpt12 = jso.GetField("opc", BindingFlags.Default).GetValue(null) as string;

                    if (sOpt9 == "color01") {
                        p.Debut = true;
                    } else if (sOpt12.IndexOf("03") != -1) {
                        p.NewFace = true;
                    }

                    p.Age = (int)jso.GetField("age", BindingFlags.Default).GetValue(null);

                    p.DonaCount = (int)jso.GetField("cnt", BindingFlags.Default).GetValue(null);
#if !NOCOMMENT
                    string ttt = jso.GetField("cha", BindingFlags.Default).GetValue(null) as string;
                    if (Pub.DebugMode)
                        if (HttpUtilityEx2.IsSurrogatePair(ttt))
                            Log.Add(Site + " - " + p.Name, "サロゲートペア文字あり", LogColor.Warning);
                    p.OtherInfo += HttpUtilityEx2.HtmlDecode(ttt);
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
            return new FormFlashAngelLive(performer);
        }
        
        public string GetFlashUrl(Performer performer) {
            //FlashのURLを返す・・
            //return "http://www.angel-live.jp/flash/peep.swf?ownerCode=49608&performerCode=" +  performer.ID;
            return "https://www.angel-live.com/common/swf/chat/male_wait_al.swf?180409&ownerCode=49608&performerCode=" + performer.ID;
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
