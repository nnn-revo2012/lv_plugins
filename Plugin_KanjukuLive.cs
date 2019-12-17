using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using LiveViewer;
using LiveViewer.Tool;
using LiveViewer.HtmlParser;

namespace Plugin_KanjukuLive {
    public class Plugin_KanjukuLive : ISitePlugin {
        #region ■定義

        private class FormFlashKanjukuLive : FormFlash {
            public Timer  Timer   = new Timer(); //メッセージ定期チェック用
            public string Message = null;        //メッセージ(前回との比較用)

            public FormFlashKanjukuLive(Performer pef)
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
                        //Log.AddMessage(Performer, sMes); //メッセージログ表示
                        Log.Add(Performer.Plugin.Site + " - " + Performer.Name, "≫" + sMes, LogColor.Pef_Message);
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

        private Parser Parser          = new Parser();    //HTML解析用パーサ

        private Regex RegexGetPef      = new Regex("aPFBoxOuter (.*)", RegexOptions.Compiled); //Data取得用
        private Regex RegexGetStatus   = new Regex("thumbStatus", RegexOptions.Compiled); //Status取得用
        private Regex RegexGetAge      = new Regex("\\(((.[0-9]*)才|秘密)\\)", RegexOptions.Compiled); //年齢取得用

        #endregion


        #region ■ISitePluginインターフェースの実装

        public string Site       { get { return "KanjukuLive"; } }

        public string Caption    { get { return "感熟ライブ用のプラグイン(2019/12/18版)"; } }

        public string TopPageUrl { get { return "http://www.kanjukulive.com/"; } }

        public void Begin() {
            //プラグイン開始時処理
        }

        public void End() {
            //プラグイン終了時処理
        }

        public List<Performer> Update() {
            List<Performer> pefs = new List<Performer>();

            try {
                Parser.UserAgent = Pub.UserAgent + "_" + Site; //User-Agentを設定
                Parser.LoadHtml(new Uri(TopPageUrl + "search?online=1"), "UTF-8");
                Parser.ParseTree();
                Pub.WebRequestCount++;
            } catch (Exception ex) {
                Log.Add(Site + "-Update失敗", ex.ToString(), LogColor.Error);
                return null;
            }

            //パフォ情報のタグを取得
            List<HtmlItem> tagTops = Parser.Find("div", "class", RegexGetPef);
            Parser.Clear();

            if (Pub.DebugMode == true ) Log.Add(Site, "tagTops.Count: " + tagTops.Count, LogColor.Warning); //DEBUG
            foreach (HtmlItem item in tagTops) {
                // ID取得
                string sID = item.Find("a", "data-pfname", true)[0].GetAttributeValue("data-pfname");
                if (Pub.DebugMode == true )
                    if (pefs.Count < 1) Log.Add(Site, "ID OK", LogColor.Warning); //DEBUG

                Performer p = new Performer(this, sID);
                p.Name = sID;
                p.ImageUrl = "https://imageup.dxlive.com/WebArchive/" + p.Name + "/live/LinkedImage.jpg";
                p.ImageUpdateCheck = false;

                // 人数
                HtmlItem tmp1 = item.Find("div", "class", "kl_icon viewers_number", true)[0];
                if (tmp1.Items.Count > 0) {
                    int.TryParse(tmp1.Items[0].Text, out p.DonaCount);
                }

                //年齢
                tmp1 = item.Find("div", "class", "pfName", true)[0];
                if (tmp1.Items.Count > 0) {
                    if (RegexGetAge.Replace(tmp1.Items[1].Text, "$1") != "秘密") {
                        if (!int.TryParse(RegexGetAge.Replace(tmp1.Items[1].Text, "$2"), out p.Age)) {
                            Log.Add(Site + " - " + p.Name, "Age: " + tmp1.Items[1].Text, LogColor.Error);
                        }
                    }
                }

                //新人
                if (item.Find("div", "class", "kl_icon newbie", true).Count > 0) {
                    p.NewFace = true;
                }
                //デビュー
                if (item.Find("div", "class", "kl_icon newbie2", true).Count > 0) {
                    p.Debut = true;
                }

                //ステータス
                string sStat = "待機中";
                if (item.Find("div", "class", RegexGetStatus, true).Count > 0) {
                    sStat = item.Find("div", "class", RegexGetStatus, true)[0].Items[0].Text;
                }
                switch (sStat) {
                    case "待機中":
                        break;
                    case "チャット中":
                        p.TwoShot = false; p.Dona = true; break;
                    case "２ショット中":
                        p.TwoShot = true; p.Dona = true; break;
                    case "完全２ショット中":
                        p.TwoShot = true; p.Dona = true; p.RoomName = "ｶﾝｾﾞﾝ2"; break;
                    default: Log.Add(Site + " - " + p.Name, "不明な状態: " +  sStat, LogColor.Error); break;
                }

#if !NOCOMMENT
                //メッセージ取得　タグの中身があるかチェック
                tmp1 = item.Find("div", "class", "thumbComment", true)[0];
                if (tmp1.Items.Count >= 2) {
                    string ttt = tmp1.Items[1].Text;
                    if (Pub.DebugMode)
                        if (HttpUtilityEx2.IsSurrogatePair(ttt))
                            Log.Add(Site + " - " + p.Name, "サロゲートペア文字あり", LogColor.Warning);
                    p.OtherInfo += HttpUtilityEx2.HtmlDecode(ttt);
                }
#endif

                if (Pub.DebugMode == true )
                    if (pefs.Count < 1) Log.Add(Site, "pefs.Add OK", LogColor.Warning); //DEBUG

                pefs.Add(p);
            }

            return pefs;
        }

        public FormFlash OpenFlash(Performer performer) {
            //フラッシュ窓を返す
            return new FormFlashKanjukuLive(performer);
        }

        public string GetFlashUrl(Performer performer) {
            //FlashのURLを返す・・待機画像ページのHTMLから取得する
            string sFlash = null;
            try {
                using (WebClient wc = new WebClient()) {
                    wc.Headers.Add(HttpRequestHeader.UserAgent, Pub.UserAgent + "_" + Site);
                    //JSONファイルを読み込んでユーザーIDをGETする
                    string sHtml = wc.DownloadString(TopPageUrl + "json/performer");
                    Regex RegexParseOnline = new Regex("\"" + performer.Name + "\":{\"user_id\":\"([^\"]*)\",\"session\":\"([^\"]*)\",\"component\":\"([^\"]*)\",\"num\":\"([^\"]*)\",\"hd\":([0-9]*)}");
                    Match m = RegexParseOnline.Match(sHtml);
                    if (m.Success) {
                        //FlashファイルのURLを作成
                        sFlash = TopPageUrl + "flash/chat/preview.swf?"; //freePreview20.swf | limitedFreePreview20.swf
                        sFlash += "from_site=1001343&performer_id=" + m.Groups[1].Value;
                        sFlash += "&performer_name=" + performer.Name;
                        sFlash += "&session_type=" + m.Groups[2].Value;
                        sFlash += "&user_type=&lang_id=jp&hd=" + m.Groups[5].Value;
                        sFlash += "&copyright=(C)2010 KANJUKULIVE.COM ALL RIGHTS RESERVED.&live_symbol=1";
                        sFlash += "&vw_count=" + m.Groups[4].Value;
                    } else {
                        //JSONファイルにないのでプロフからURL取得
                        Log.Add(Site + " - " + performer.Name, "JSONにIDなし", LogColor.Error);
                        sHtml = wc.DownloadString(TopPageUrl + "profile/" + performer.Name + "/preview");
                        Regex RegexGetSwf = new Regex("fillswf\\('" + performer.Name + "', +([0-9]*)\\)", RegexOptions.Compiled); //Swf表示用 2010/04/25
                        if (RegexGetSwf.Match(sHtml).Groups[1].Value != null) {
                            sFlash = TopPageUrl + "flash/chat/preview.swf?"; //freePreview20.swf | limitedFreePreview20.swf
                            sFlash += "from_site=1001343&performer_id=" + RegexGetSwf.Match(sHtml).Groups[1].Value;
                            sFlash += "&performer_name=" + performer.Name;
                            sFlash += "&session_type=";
                            sFlash += "&user_type=&lang_id=jp&hd=0";
                            sFlash += "&copyright=(C)2010 KANJUKULIVE.COM ALL RIGHTS RESERVED.&live_symbol=1";
                            sFlash += "&vw_count=";
                        }
                    }
                    sFlash += "&width=654&height=491";
                    sFlash += "&kl_thumb=1&hide_msg=3";
                    Pub.WebRequestCount++; //GUIの読込回数を増やす
                }
            } catch (Exception ex) {
                Log.Add(Site + "-GetFlashUrl失敗", ex.ToString(), LogColor.Error);
            }
            //Clipboard.SetText(sFlash);
            return sFlash;
        }

        public Clipping GetFlashClipping(Performer performer) {
            //Flashの切り抜き方法を返す
            Clipping c = new Clipping();
            c.OriginalSize.Width  = 654;  //フラッシュ全体の幅
            c.OriginalSize.Height = 491;  //フラッシュ全体の高さ
            c.ClippingRect.X      = 7;   //切り抜く領域の左上座標(X)
            c.ClippingRect.Y      = 7;   //切り抜く領域の左上座標(Y)
            c.ClippingRect.Width  = 640;  //切り抜く領域の幅
            c.ClippingRect.Height = 480;  //切り抜く領域の高さ
            c.Fixed               = true; //Flashが固定サイズ
            return c;
        }

        public string GetProfileUrl(Performer performer) {
            //プロフィールURLを返す
            return TopPageUrl + "profile/" + performer.ID + "/preview";
        }

        #endregion
    }
}
