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

namespace Plugin_DxLive {
    public class Plugin_DxLive : ISitePlugin {
        #region ■定義

        private class FormFlashDxLive : FormFlash {
            public Timer  Timer   = new Timer(); //メッセージ定期チェック用
            public string Message = null;        //メッセージ(前回との比較用)

            public FormFlashDxLive(Performer pef)
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

        private Regex RegexGetID       = new Regex("/(preview|profile)/(.*)", RegexOptions.Compiled); //ID取得用 2014/06/13
        private Regex RegexGetStatus   = new Regex("nl-th-thumbBox +([^\"]*)", RegexOptions.Compiled); //Status取得用 2014/01/04
        private Regex RegexGetSwf      = new Regex("var[ \\t\\n]+FlashVars[ \\t\\n]*=[ \\t\\n]*\"([^\"]*)\"", RegexOptions.Compiled); //Swf表示用 2010/04/25
        private Regex RegexGetPfid     = new Regex("var[ \\t\\n]+pf_id[ \\t\\n]*=[ \\t\\n]*\"([^\"]*)\"", RegexOptions.Compiled); //pf_id表示用 2019/12/17

        #endregion


        #region ■ISitePluginインターフェースの実装

        public string Site       { get { return "DXlive"; } }

        public string Caption    { get { return "DXLive用のプラグイン(2020/01/21版)"; } }

        public string TopPageUrl { get { return "https://www.dxlive.com/"; } }

        public void Begin() {
            //プラグイン開始時処理
        }

        public void End() {
            //プラグイン終了時処理
        }

        public List<Performer> Update() {
            List<Performer> pefs = new List<Performer>();
            List<string> pefs2Shot = new List<string>();
            string resData;

            //パフォーマー全員
            try {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | (SecurityProtocolType)0x00000C00 | (SecurityProtocolType)0x00000300;
                using (WebClient wc = new WebClient()) {
                    //WebからJSONデータを取得する(GET)
                    wc.Headers.Add(HttpRequestHeader.UserAgent, Pub.UserAgent + "_" + Site);
                    wc.Headers.Add(HttpRequestHeader.Referer, TopPageUrl);
                    wc.Encoding = Encoding.UTF8;
                    resData = wc.DownloadString(TopPageUrl + "search?online=1&order_by=enc_band&nl=1&hide_pf_guest=1&coupon=0");
                }
                Pub.WebRequestCount++;
            } catch (Exception ex) {
                Log.Add(Site + "-Update失敗", ex.ToString(), LogColor.Error);
                return null;
            }

            Parser.LoadHtml(resData);
            Parser.ParseTree();
            //パフォ情報のタグを取得
            List<HtmlItem> tagTops = Parser.Find("div", "class", RegexGetStatus);
            Parser.Clear();

            if (Pub.DebugMode == true ) Log.Add(Site, "tagTops.Count: " + tagTops.Count, LogColor.Warning); //DEBUG
            foreach (HtmlItem item in tagTops) {
                //2010/04/25 11文字以上のIDだと名前が...で省略されるんで a href ... から取得するように変更
                string sID = RegexGetID.Match(item.Find("a", "class", "nl-th-lowerPart", true)[0].GetAttributeValue("href")).Groups[2].Value;
                if (Pub.DebugMode == true )
                    if (pefs.Count < 1) Log.Add(Site, "ID OK", LogColor.Warning); //DEBUG

                Performer p = new Performer(this, sID);
                p.Name = sID;
                p.ImageUrl = "https://imageup.dxlive.com/WebArchive/" + p.Name + "/live/LinkedImage.jpg";
                p.ImageUpdateCheck = false;

                // 人数
                int num;
                if (item.Find("span", "class", "nl-th-vwrCnt", true).Count > 0) {
                    HtmlItem tmp1 = item.Find("span", "class", "nl-th-vwrCnt", true)[0];
                    if (tmp1.Items.Count > 1) {
                        if (int.TryParse(tmp1.Items[1].Text, out num))
                            p.DonaCount = num;
                    }
                }

                // 新人とデビューのステータス
                if (item.Find("span", "class", "nl-th-icon-super-newbie nl-th-icon", true).Count > 0) {
                    p.Debut = true;
                }
                if (item.Find("span", "class", "nl-th-icon-newbie nl-th-icon", true).Count > 0) {
                    p.NewFace = true;
                }

#if !NOCOMMENT
                if (item.Find("li", "class", "nl-th-pfInfoTxt", true).Count > 0) {
                    HtmlItem tmp1 = item.Find("li", "class", "nl-th-pfInfoTxt", true)[0];
                    if (tmp1.Items.Count > 0) {
                        string ttt = tmp1.Items[0].Text;
                        if (Pub.DebugMode)
                            if (HttpUtilityEx2.IsSurrogatePair(ttt))
                                Log.Add(Site + " - " + p.Name, "サロゲートペア文字あり", LogColor.Warning);
                        p.OtherInfo += HttpUtilityEx2.HtmlDecode(ttt);
                    }
                }
#endif

                //ステータス
                string sStat = item.GetAttributeValue("class");
                switch (RegexGetStatus.Match(sStat).Groups[1].Value) {
                    case "standby ":
                    case "freechat ":
                        p.TwoShot = false; p.Dona = false; break;
                    case "session ":
                        p.TwoShot = false; p.Dona = true; break;
                    case "toy nl-blink1":
                        p.TwoShot = false; p.Dona = true; p.RoomName = "ﾊﾞｲﾌﾞ"; break;
                    case "toy nl-blink2":
                        p.TwoShot = false; p.Dona = true; p.RoomName = "2ﾊﾞｲﾌﾞ"; break;
                    case "toy nl-blink3":
                        p.TwoShot = false; p.Dona = true; p.RoomName = "3ﾊﾞｲﾌﾞ"; break;
                    case "twoshot ":
                        p.TwoShot = true; p.Dona = true;  break;
                    default: Log.Add(Site + " - " + p.Name, "不明な状態: " + item.GetAttributeValue("class"), LogColor.Error); break;
                }

                if (Pub.DebugMode == true )
                    if (pefs.Count < 1) Log.Add(Site, "pefs.Add OK", LogColor.Warning); //DEBUG

                pefs.Add(p);
            }

            return pefs;
        }

        public FormFlash OpenFlash(Performer performer) {
            //フラッシュ窓を返す
            return new FormFlashDxLive(performer);
        }

        public string GetFlashUrl(Performer performer) {
            //2010/04/25 最初JSONファイルを読み込んでIDを探す。IDがない場合だけプロフを読むよう変更（負荷対策？）
            //FlashのURLを返す・・JSONファイルまたは待機画像ページのHTMLから取得する
//https://www.dxlive.com/flash/chat/preview.swf?channel=75404947&from_site=1000048&performer_id=75404947
//&performer_name=xNANAKAx20&session_type=115&vw_count=1&lang_id=jp&userSiteID=1000048&skinName=&user_type=
//&copyright=(C)2002 DXLIVE.COM ALL RIGHTS RESERVED.
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
                        sFlash  = TopPageUrl + "flash/chat/freePreview20.swf?"; //freePreview20.swf | limitedFreePreview20.swf
                        sFlash += "channel=" + m.Groups[1].Value + "&performerID=" + m.Groups[1].Value + "&langID=jp";
                        /*
                        sFlash = TopPageUrl + "flash/chat/preview.swf?"; //freePreview20.swf | limitedFreePreview20.swf
                        sFlash += "channel=" + m.Groups[1].Value;
                        sFlash += "&from_site=1000048&performer_id=" + m.Groups[1].Value;
                        sFlash += "&performer_name=" + performer.Name;
                        sFlash += "&session_type=" + m.Groups[2].Value;
                        sFlash += "&vw_count=" + m.Groups[4].Value + "&lang_id=jp&userSiteID=1000048&skinName=&user_type=";
                        sFlash += "&copyright=(C)2002 DXLIVE.COM ALL RIGHTS RESERVED.";
                        */
                    } else {
                        //JSONファイルにないのでプロフからURL取得
                        Log.Add(Site + " - " + performer.Name, "JSONにIDなし", LogColor.Error);
                        sHtml = wc.DownloadString(TopPageUrl + "preview/" + performer.Name);
                        if (RegexGetPfid.Match(sHtml).Groups[1].Value != null) {
                            sFlash = TopPageUrl + "flash/chat/freePreview20.swf?"; //freePreview20.swf | limitedFreePreview20.swf
                            sFlash += "channel=" + RegexGetPfid.Match(sHtml).Groups[1].Value + "&performerID=" + RegexGetPfid.Match(sHtml).Groups[1].Value + "&langID=jp";
                        //if (RegexGetSwf.Match(sHtml).Groups[1].Value != null) {
                        //    sFlash = TopPageUrl + "flash/chat/preview.swf?"; //freePreview20.swf | limitedFreePreview20.swf
                        //    sFlash += RegexGetSwf.Match(sHtml).Groups[1].Value;
                        }
                    }
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
            return null;
        }

        public string GetProfileUrl(Performer performer) {
            return TopPageUrl + "preview/" + performer.ID;
        }

        #endregion
    }
}
