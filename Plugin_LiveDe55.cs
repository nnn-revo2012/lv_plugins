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

namespace Plugin_LiveDe55 {
    public class Plugin_LiveDe55 : ISitePlugin {
        #region ■定義

        private class FormFlashLiveDe55 : FormFlash {

            public FormFlashLiveDe55(Performer pef)
                : base(pef) {
            }

        }

        private class WebClientEx : WebClient {
            protected override WebRequest GetWebRequest(Uri address) {
                WebRequest wr = base.GetWebRequest(address);
                HttpWebRequest hwr = wr as HttpWebRequest;
                if (hwr != null) {
                    hwr.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate; //圧縮を有効化
                }
                return wr;
            }
        }

        #endregion


        #region ■オブジェクト
        Parser Parser = new Parser();    //HTML解析用パーサ

        private Regex RegexGetStatus   = new Regex("^lady +", RegexOptions.Compiled); //Status取得用
        private Regex RegexGetID       = new Regex("/([0-9]+)$", RegexOptions.Compiled); //パフォID取得用
        private Regex RegexGetAge      = new Regex("([0-9]+)歳", RegexOptions.Compiled); //年齢取得用
        private Regex RegexGetDSPef    = new Regex("^lady-main-ds +list__item +", RegexOptions.Compiled); //DSパフォデータ取得用
        private Regex RegexGetDSID     = new Regex("p[0-9]*-([0-9]+)", RegexOptions.Compiled); //DSパフォID取得用
        private Regex RegexGetDSImg    = new Regex("background-image:url\\(([^\\)]*)\\)", RegexOptions.Compiled); //DSパフォIMG取得用
        private Regex RegexGetSwf1     = new Regex("embed[ \\t\\n]+src[ \\t\\n]*=[ \\t\\n]*\"([^\"]*)\"", RegexOptions.Compiled); //Swf表示用1
        private Regex RegexGetSwf2     = new Regex("flashvars[ \\t\\n]*=[ \\t\\n]*\"([^\"]*)\"", RegexOptions.Compiled); //Swf表示用2

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

        public string Site       { get { return "livede55"; } }

        public string Caption    { get { return "ライブでゴーゴー用のプラグイン(2019/03/22版)"; } }

        public string TopPageUrl { get { return "https://livede55.com/"; } }

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
        }

        public void End() {
            //プラグイン終了時処理
        }

        public List<Performer> Update() {
            List<Performer> pefs = new List<Performer>();
            JSObject top = new JSObject();

            try {
                //データー取得
                using (WebClientEx Client = new WebClientEx()) {
                    //WebからJSONデータを取得する(GET)
                    Client.Headers.Add(HttpRequestHeader.UserAgent, Pub.UserAgent + "_" + Site);
                    Client.Headers.Add(HttpRequestHeader.Referer, TopPageUrl);
                    Client.Headers.Add("X-Requested-With","XMLHttpRequest");
                    Client.Headers.Add("Accept-Language","ja,en-US;q=0.8,en;q=0.6");

                    byte[] resArray = Client.DownloadData(TopPageUrl + "ajax/top/all-performers-with-ds");
                    string resData = "(" + Encoding.UTF8.GetString(resArray) + ");";
                    //string resData = ("({\"result\":false,\"html\":\"abcdef\",\"banner_html\":\"\"});");

                    //Jsファイルの内容を実行する
                    top = JsExecuterType.InvokeMember("Eval", BindingFlags.InvokeMethod, null, JsExecuterObject, new object[] { resData }) as JSObject;
                }

                //普通のチャット
                string sHtml = top.GetField("online_html", BindingFlags.Default).GetValue(null).ToString();
                Parser.LoadHtml(sHtml);
                Parser.ParseTree();

                //パフォ情報のタグを取得
                List<HtmlItem> tagTops = Parser.Find("section", "class", RegexGetStatus);
                Parser.Clear();
                if (Pub.DebugMode == true ) Log.Add(Site, "tagTops.Count: " + tagTops.Count, LogColor.Warning); //DEBUG

                //パフォ1人毎
                foreach (HtmlItem item in tagTops) {

                    if (item.Find("a", "class", "lady-link", true).Count < 1) {
                        Log.Add(Site + " - " + "???", "ID不明", LogColor.Error);
                        continue;
                    }
                    string sTmp = item.Find("a", "class", "lady-link", true)[0].GetAttributeValue("href");
                    string sID = RegexGetID.Match(sTmp).Groups[1].Value;
                    if (Pub.DebugMode == true )
                        if (pefs.Count < 1) Log.Add(Site, "ID OK", LogColor.Warning); //DEBUG

                    Performer p = new Performer(this, sID);

                    if (item.Find("h3", "class", "lady-main__name", true).Count > 0) {
                        if (item.Find("h3", "class", "lady-main__name", true)[0].TryGetSubItemText(out sTmp, 0)) {
                            p.Name = HttpUtilityEx.HtmlDecode(sTmp);
                        } else {
                           Log.Add(Site + " - " + sID, "名前なし", LogColor.Warning);
                        }
                    } else {
                        Log.Add(Site + " - " + sID, "名前なし", LogColor.Warning);
                    }

                    if (item.Find("div", "class", "lady-pic__image", true).Count > 0) {
                        p.ImageUrl = item.Find("div", "class", "lady-pic__image", true)[0].GetAttributeValue("data-echo-background");
                        p.ImageUpdateCheck = false;
                    } else {
                        Log.Add(Site + " - " + p.Name, "画像なし", LogColor.Error);
                    }

                    // 年齢
                    if (item.Find("span", "class", "lady-main__age", true).Count > 0) {
                        p.Age = int.Parse(RegexGetAge.Match(item.Find("span", "class", "lady-main__age", true)[0].Items[0].Text).Groups[1].Value);
                    }

                    // ステータス
                    string sStat = item.GetAttributeValue("class");

                    // 新人とデビューのステータス
                    switch (Regex.Match(sStat, "status_(new|brandnew)", RegexOptions.Compiled).Groups[1].Value) {
                        case "brandnew": // デビュー
                            p.Debut = true; break;
                        case "new":      // 新人
                           p.NewFace = true; break;
                        default: break;
                    }

                    // スマホ
                    if (sStat.IndexOf("for_spphone") >= 0) p.RoomName = "ｽﾏﾎ";

                    switch (Regex.Match(sStat, "status_(offline|wait|party|2shot|meet)", RegexOptions.Compiled).Groups[1].Value) {
                        case "offline": // オフライン
                            continue;
                        case "wait": // 待機中
                            p.TwoShot = false; p.Dona = false; break;
                        case "party": // パーティ中
                            p.TwoShot = false; p.Dona = true; break;
                        case "2shot": // ２ショット中
                            p.TwoShot = true; p.Dona = true;  break;
                        case "meet": // 待ち合わせ
                            p.TwoShot = false; p.Dona = false; p.OtherInfo += "待ち合わせ "; break;
                        default: Log.Add(Site + " - " + p.Name, "不明な状態: " + item.GetAttributeValue("class"), LogColor.Error); break;
                    }

                    /*
                    // メッセージ取得
                    if (item.Find("p", "class", "comment-wrap__txt", true).Count > 0) {
                        if (item.Find("p", "class", "comment-wrap__txt", true)[0].Items.Count > 0) {
                            p.OtherInfo += HttpUtilityEx.HtmlDecode(item.Find("p", "class", "comment-wrap__txt", true)[0].Items[0].Text);
                        }
                    }
                    */

                    if (Pub.DebugMode == true )
                        if (pefs.Count < 1) Log.Add(Site, "pefs.Add OK", LogColor.Warning); //DEBUG

                    pefs.Add(p);
                }

                //ＤＳチャット
                sHtml = top.GetField("ds_html", BindingFlags.Default).GetValue(null).ToString();
                Parser.LoadHtml(sHtml);
                Parser.ParseTree();

                //パフォ情報のタグを取得
                tagTops = Parser.Find("section", "class", RegexGetStatus);
                Parser.Clear();
                if (Pub.DebugMode == true ) Log.Add(Site, "tagTops.Count: " + tagTops.Count, LogColor.Warning); //DEBUG

                //DSの1ペア毎
                foreach (HtmlItem item in tagTops) {
                    List<HtmlItem> dstagTops = item.Find("li", "class", RegexGetDSPef, true);
                    if (Pub.DebugMode == true ) Log.Add(Site, "dstagTops.Count: " + dstagTops.Count, LogColor.Warning); //DEBUG

                    //DSのパフォ1人毎
                    foreach (HtmlItem item2 in dstagTops) {

                        if (item.Find("div", "class", "lady-pic__image", true).Count < 1) {
                            Log.Add(Site + " - " + "", "DS ID不明", LogColor.Error);
                            continue;
                        }
                        string sTmp = item2.Find("div", "class", "lady-pic__image", true)[0].GetAttributeValue("style");
                        string sID = RegexGetDSID.Match(sTmp).Groups[1].Value;
                        if (Pub.DebugMode == true )
                            if (pefs.Count < 1) Log.Add(Site, "DS ID OK", LogColor.Warning); //DEBUG

                        Performer p = new Performer(this, sID);

                        p.ImageUrl = RegexGetDSImg.Match(sTmp).Groups[1].Value;
                        p.ImageUpdateCheck = false;

                        if (item.Find("h3", "class", "lady-main__name", true).Count > 0) {
                            if (item.Find("h3", "class", "lady-main__name", true)[0].TryGetSubItemText(out sTmp, 0)) {
                                p.Name = HttpUtilityEx.HtmlDecode(sTmp);
                            } else {
                               Log.Add(Site + " - " + sID, "名前なし", LogColor.Warning);
                            }
                        } else {
                            Log.Add(Site + " - " + sID, "名前なし", LogColor.Warning);
                        }

                        // DSの場合
                        if (item.Find("a", "class", "lady-main", true).Count > 0) {
                            p.OtherInfo = Regex.Match(item.Find("a", "class", "lady-main", true)[0].GetAttributeValue("href"), "(ds[0-9]+)",  RegexOptions.Compiled).Groups[1].Value + " ";
                        }
                        p.RoomName  = "DS";

                        // ステータス
                        string sStat = item.GetAttributeValue("class");

                        switch (Regex.Match(sStat, "status_(offline|wait|party|2shot|meet)", RegexOptions.Compiled).Groups[1].Value) {
                            case "offline": // オフライン
                                continue;
                            case "wait": // 待機中
                                p.TwoShot = false; p.Dona = false; break;
                            case "party": // パーティ中
                                p.TwoShot = false; p.Dona = true; break;
                            case "2shot": // ２ショット中
                                p.TwoShot = true; p.Dona = true;  break;
                            case "meet": // 待ち合わせ
                                p.TwoShot = false; p.Dona = false; p.OtherInfo += "待ち合わせ "; break;
                            default: Log.Add(Site + " - " + p.Name, "不明な状態: " + item.GetAttributeValue("class"), LogColor.Error); break;
                        }

                        if (Pub.DebugMode == true )
                            if (pefs.Count < 1) Log.Add(Site, "DS pefs.Add OK", LogColor.Warning); //DEBUG

                        pefs.Add(p);
                    } //DSのパフォ1人毎

                } //DSの1ペア毎
            }
            catch (Exception ex) {
                Log.Add(Site + "-Update失敗", ex.ToString(), LogColor.Error);
                return null;
            }

            return pefs;
        }

        public FormFlash OpenFlash(Performer performer) {
            //フラッシュ窓を返す
            return new FormFlashLiveDe55(performer);
        }

        public string GetFlashUrl(Performer performer) {
            //FlashのURLを返す・・待機画像ページのHTMLから取得する
            string sFlash = null;
            try {
                using (WebClientEx wc = new WebClientEx()) {
                    wc.Headers.Add(HttpRequestHeader.UserAgent, Pub.UserAgent + "_" + Site);
                    wc.Headers.Add("Accept-Language","ja,en-US;q=0.8,en;q=0.6");
                    wc.Encoding = Encoding.UTF8;
                    string sHtml = wc.DownloadString(TopPageUrl + "chat/performer/" + performer.ID);
                    if (RegexGetSwf1.Match(sHtml).Groups[1].Value != null) {
                        sFlash = RegexGetSwf1.Match(sHtml).Groups[1].Value;
                        if (sFlash.IndexOf('?') >= 0) {
                            sFlash += "&" + HttpUtilityEx.HtmlDecode(RegexGetSwf2.Match(sHtml).Groups[1].Value);
                        } else {
                            sFlash += "?" + HttpUtilityEx.HtmlDecode(RegexGetSwf2.Match(sHtml).Groups[1].Value);
                        }
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
            c.OriginalSize.Width  = 938;  //フラッシュ全体の幅
            c.OriginalSize.Height = 413;  //フラッシュ全体の高さ
            c.ClippingRect.X      = 4;   //切り抜く領域の左上座標(X)
            c.ClippingRect.Y      = 10;   //切り抜く領域の左上座標(Y)
            c.ClippingRect.Width  = 640;  //切り抜く領域の幅
            c.ClippingRect.Height = 360;  //切り抜く領域の高さ
            c.Fixed               = true; //Flashが固定サイズ
            return c;
        }

        public string GetProfileUrl(Performer performer) {
            //string sProfUrl = TopPageUrl + "performer/profile/p" + performer.ID;
            string sProfUrl = TopPageUrl + "chat/performer/" + performer.ID;
            if (performer.RoomName == "DS") {
                string tmp1 = Regex.Match(performer.OtherInfo, "(ds[0-9]+)", RegexOptions.Compiled).Groups[1].Value;
                if (tmp1 != "") sProfUrl = TopPageUrl + "performer/profile/" + tmp1;
            }
            return sProfUrl;
        }

        #endregion
    }
}
