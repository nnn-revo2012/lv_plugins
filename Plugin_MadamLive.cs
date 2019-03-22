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

namespace Plugin_MadamLive {
    public class Plugin_MadamLive : ISitePlugin {
        #region ■定義

        private class FormFlashMadamLive : FormFlash {
            public FormFlashMadamLive(Performer pef)
                : base(pef) {
            }
        }

        #endregion


        #region ■オブジェクト

        private WebClient Client       = new WebClient(); //HTML取得用

        //HTML解析用の正規表現
        private Regex RegexGetSwf = new Regex("flash\\('/(flash/m_shityo[^']+)'", RegexOptions.Compiled);

        private string[] sRoomName = new string[]{"ev_online_girls", "online_girls", "etc_online_girls"};

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

        public string Site       { get { return "madamlive"; } }

        public string Caption    { get { return "MadamLive用のプラグイン(2018/07/31版)"; } }

        public string TopPageUrl { get { return "http://www.madamlive.tv/"; } }

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
                using (Stream       s  = Client.OpenRead(TopPageUrl + "ajax/new_online_girls_ajax.php?"))
                using (StreamReader sr = new StreamReader(s, Encoding.GetEncoding("EUC-JP"))) {
                    sb.Append(sr.ReadToEnd());
                }
                sb.Append(");");
                sb.Replace("\r\n", "");
                //サーバーメンテ処理＆データー取得エラー処理
                if (!Regex.IsMatch(sb.ToString(), "\"success\":\"true\"}\\);$")) {
                    throw new WebException("Webからデーターを取得できませんでした");
                }

                //Jsファイルの内容を実行する
                JSObject    top = JsExecuterType.InvokeMember("Eval", BindingFlags.InvokeMethod, null, JsExecuterObject, new object[] { sb.ToString() }) as JSObject;

                //ルーム毎にデーターを読み込む
                foreach (string sRoom in sRoomName) {
                    ArrayObject obj = top.GetField(sRoom, BindingFlags.Default).GetValue(null) as ArrayObject;
                    for (int i = 0; i < (int)obj.length; i++) {
                        JSObject jso = new JSObject();
                        jso = (JSObject)obj[i];
                        jso.AddField("room");
                        jso.SetMemberValue2("room", sRoom); //データーに部屋名を追加
                        jso2.Add(jso);
                    }
                }
            } catch (WebException ex){
                //読み込み失敗(Web関連)
                Log.Add(Site + "-Update失敗(Web)", ex.ToString(), LogColor.Error);
                return pefs;
            } catch (Exception ex) {
                Log.Add(Site + "-Update失敗", ex.ToString(), LogColor.Error);
                return null;
            }

            if (Pub.DebugMode == true ) Log.Add(Site, "jso2.Count: " + jso2.Count, LogColor.Warning); //DEBUG
            //データーを読み込んで pefs に追加
            foreach (JSObject jso in jso2) {
                string    sID = jso.GetField("hash", BindingFlags.Default).GetValue(null) as string;
                if (Pub.DebugMode == true )
                    if (pefs.Count < 1) Log.Add(Site, "ID OK", LogColor.Warning); //DEBUG
                Performer p   = new Performer(this, sID);
                p.Name        = HttpUtilityEx.HtmlDecode(jso.GetField("nick_name", BindingFlags.Default).GetValue(null) as string);
                string sImg = jso.GetField("img", BindingFlags.Default).GetValue(null) as string;
                p.ImageUrl = sImg.Replace("/girl_img/", "/girl_img/120x90/"); //2015/05/20
                p.ImageUpdateCheck = false;
                if (Pub.DebugMode == true )
                    if (pefs.Count < 1) Log.Add(Site, p.ImageUrl, LogColor.Warning); //DEBUG

                string sStatus = jso.GetField("opt_status", BindingFlags.Default).GetValue(null) as string;
                switch (sStatus) {
                    case "standby": //オンライン
                        p.Dona = false; p.TwoShot = false; break;
                    case "showtime": //チャット中
                        p.Dona = true; p.TwoShot = false; break;
                    case "party":    //待機
                        p.Dona = false; p.TwoShot = false; break;
                    case "inparty":  //チャット中
                        p.Dona = true; p.TwoShot = false; break;
                    case "twoshot":  //２ショット
                        p.Dona = true; p.TwoShot = true; break;
                    case "meeting":  //待ち合わせ
                        p.Dona = false; p.TwoShot = false; p.OtherInfo = "待合せ中 "; break;
                    case "junbi": //準備中 2018/07/31追加
                        continue;
                    default: Log.Add(Site + "-ERROR", "不明な状態 " + p.Name + ": st=" + sStatus ,LogColor.Error); break;
                }

                //部屋の取得
                string sRoom = jso.GetField("room", BindingFlags.Default).GetValue(null) as string;
                switch (sRoom) {
                    case "ev_online_girls":
                        p.RoomName = "ｲﾍﾞﾝﾄ"; break;
                    default: break;
                }

                string sNewFace = jso.GetField("new_face", BindingFlags.Default).GetValue(null) as string;
                switch (sNewFace) {
                    case "":
                        break;
                    case "-newface":
                    case "-newface3":
                        p.Debut = true; break;
                    case "-newface2":
                        p.NewFace = true; break;
                    default:
                        Log.Add(Site + "-ERROR", "新人 " + p.Name + ": nf=" + sNewFace ,LogColor.Error);break;
                }

                string sSec = jso.GetField("sec_1", BindingFlags.Default).GetValue(null).ToString();
                if (sSec.Length > 0) {
                    p.DonaCount = int.Parse(sSec);
                } else {
                    Log.Add(Site + "-ERROR", "人数 " + p.Name + ": sec_1=" + sSec ,LogColor.Error);
                }

                p.OtherInfo += HttpUtilityEx.HtmlDecode(jso.GetField("taiki_comment", BindingFlags.Default).GetValue(null) as string);

                if (Pub.DebugMode == true )
                    if (pefs.Count < 1) Log.Add(Site, "pefs.Add OK", LogColor.Warning); //DEBUG

                pefs.Add(p);
            }
            return pefs;
        }

        public FormFlash OpenFlash(Performer performer) {
            //フラッシュ窓を返す
            return new FormFlashMadamLive(performer);
        }
        
        public string GetFlashUrl(Performer performer) {
            //FlashのURLを返す・・待機画像ページのHTMLから取得する
            string sFlash = null;
            try {
                using (WebClient wc = new WebClient()) {
                    wc.Headers.Add(HttpRequestHeader.UserAgent, Pub.UserAgent + "_" + Site);
                    string sHtml = wc.DownloadString(TopPageUrl + "chat.php?id=" + performer.ID);
                    if (RegexGetSwf.Match(sHtml).Groups[1].Value != null) {
                        sFlash = TopPageUrl + RegexGetSwf.Match(sHtml).Groups[1].Value;
                    }
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
            c.OriginalSize.Width  = 695;  //フラッシュ全体の幅
            c.OriginalSize.Height = 410;  //フラッシュ全体の高さ
            c.ClippingRect.X      = 22;   //切り抜く領域の左上座標(X)
            c.ClippingRect.Y      = 8;   //切り抜く領域の左上座標(Y)
            c.ClippingRect.Width  = 640;  //切り抜く領域の幅
            c.ClippingRect.Height = 400;  //切り抜く領域の高さ
            c.Fixed               = false; //Flashが固定サイズ
            return c;
        }

        public string GetProfileUrl(Performer performer) {
            //プロフィールURLを返す
            return TopPageUrl + "chat.php?id=" + performer.ID;
        }

        #endregion
    }
}
