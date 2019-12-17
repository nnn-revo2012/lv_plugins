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

namespace Plugin_Caribbeancomgirl {
    public class Plugin_Caribbeancomgirl : ISitePlugin {
        #region ■定義

        private class FormFlashCaribbeancomgirl : FormFlash {
            public Timer  Timer   = new Timer(); //メッセージ定期チェック用
            public string Message = null;        //メッセージ(前回との比較用)

            public FormFlashCaribbeancomgirl(Performer pef)
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

        private Regex RegexGetID       = new Regex("open(Profile|Preview)\\('([^']*)'\\)", RegexOptions.Compiled); //ID取得用
        private Regex RegexGetPefs     = new Regex("thumbnails pf_thumb", RegexOptions.Compiled); //Status取得用
        private Regex RegexGetStatus   = new Regex("pf_([a-zA-Z0-9]+) ?", RegexOptions.Compiled); //Status取得用
        private Regex RegexGetSwf      = new Regex("var[ \\t\\n]+flash_var[ \\t\\n]*=[ \\t\\n]*'([^']*)'", RegexOptions.Compiled); //Swf表示用

        #endregion


        #region ■ISitePluginインターフェースの実装

        public string Site       { get { return "caribbeancomgirl"; } }

        public string Caption    { get { return "カリビアンコムガール用のプラグイン(2019/12/18版)"; } }

        public string TopPageUrl { get { return "https://www.caribbeancomgirl.com/"; } }

        public void Begin() {
            //プラグイン開始時処理
        }

        public void End() {
            //プラグイン終了時処理
        }

        public List<Performer> Update() {
            List<Performer> pefs = new List<Performer>();
            List<string> pefs2Shot = new List<string>();

            //2ShotのパフォーマーIDのみリストアップ
            try {
                Parser.UserAgent = Pub.UserAgent + "_" + Site; //User-Agentを設定
                Parser.LoadHtml(new Uri(TopPageUrl + "search?online=1&session_type=125,130,135"), "UTF-8");
                Parser.ParseTree();
                Pub.WebRequestCount++;
            } catch (Exception ex) {
                Log.Add(Site + "-Update失敗", ex.ToString(), LogColor.Error);
                return null;
            }

            List<HtmlItem> tag2Shot = Parser.Find("dl", "class", RegexGetPefs);
            Parser.Clear();
            if (Pub.DebugMode == true ) Log.Add(Site, "tag2Shot.Count: " + tag2Shot.Count, LogColor.Warning); //DEBUG
            foreach (HtmlItem item in tag2Shot) {
                HtmlItem tmp1 = item.Find("div", "class",  new Regex("pf_name(_v)?", RegexOptions.Compiled), true)[0];
                string sID = RegexGetID.Match(tmp1.Find("a", "onClick", true)[0].GetAttributeValue("onClick")).Groups[2].Value;
                pefs2Shot.Add(sID);
            }

            //パフォーマー全員
            try {
                Parser.UserAgent = Pub.UserAgent + "_" + Site; //User-Agentを設定
                Parser.LoadHtml(new Uri(TopPageUrl + "search?online=1&session_type=110,115,120,125,130,135"), "UTF-8");
                Parser.ParseTree();
                Pub.WebRequestCount++;
            } catch (Exception ex) {
                Log.Add(Site + "-Update失敗", ex.ToString(), LogColor.Error);
                return null;
            }

            //パフォ情報のタグを取得
            List<HtmlItem> tagTops = Parser.Find("dl", "class", RegexGetPefs);
            Parser.Clear();

            if (Pub.DebugMode == true ) Log.Add(Site, "tagTops.Count: " + tagTops.Count, LogColor.Warning); //DEBUG
            foreach (HtmlItem item in tagTops) {
                HtmlItem tmp1 = item.Find("div", "class",  new Regex("pf_name(_v)?", RegexOptions.Compiled), true)[0];
                string sID = RegexGetID.Match(tmp1.Find("a", "onClick", true)[0].GetAttributeValue("onClick")).Groups[2].Value;
                if (Pub.DebugMode == true )
                    if (pefs.Count < 1) Log.Add(Site, "ID OK", LogColor.Warning); //DEBUG

                Performer p = new Performer(this, sID);
                p.Name = sID;
                p.ImageUrl = "https://imageup.dxlive.com/WebArchive/" + p.Name + "/live/LinkedImage.jpg";
                p.ImageUpdateCheck = false;

                // 人数
                if (item.Find("span", "class", "num", true).Count > 0) {
                    tmp1 = item.Find("span", "class", "num", true)[0];
                    p.DonaCount = int.Parse(tmp1.Items[0].Text);
                }

                // 新人とデビューのステータス
                tmp1 = item.Find("div", "class", "pf_icons", true)[0];
                if (tmp1.Find("img", "src", new Regex("icon_fg4Final", RegexOptions.Compiled), true).Count > 0) {
                    p.Debut = true;
                }
                if (tmp1.Find("img", "src", new Regex("icon_new_girl", RegexOptions.Compiled), true).Count > 0) {
                    p.NewFace = true;
                }
                if (tmp1.Find("img", "src", new Regex("icon_NH", RegexOptions.Compiled), true).Count > 0) {
                    p.OtherInfo = "NH ";
                }

                //ステータス
                string sStat = item.Find("dt", "class", RegexGetStatus, true)[0].GetAttributeValue("class");
                switch (RegexGetStatus.Match(sStat).Groups[1].Value) {
                    case "standby":
                        p.TwoShot = false; p.Dona = false; break;
                    case "freechat":
                        p.TwoShot = false; p.Dona = false; break;
                    case "session":
                        p.TwoShot = false; p.Dona = true; break;
                    case "2shot":
                        p.TwoShot = true; p.Dona = true;  break;
                    case "private":
                        p.TwoShot = true; p.Dona = true; p.RoomName = "ｶﾝｾﾞﾝ2"; break;
                    case "vibe":
                        p.TwoShot = pefs2Shot.Exists(delegate (string s) {return s == sID;}); //チャットor2ショット判定
                        p.Dona = true; p.RoomName = "ﾊﾞｲﾌﾞ"; break;
                    case "wvibe":
                        p.TwoShot = pefs2Shot.Exists(delegate (string s) {return s == sID;}); //チャットor2ショット判定
                        p.Dona = true; p.RoomName = "2ﾊﾞｲﾌﾞ"; break;
                    case "tvibe":
                        p.TwoShot = pefs2Shot.Exists(delegate (string s) {return s == sID;}); //チャットor2ショット判定
                        p.Dona = true; p.RoomName = "3ﾊﾞｲﾌﾞ"; break;
                    default: Log.Add(Site + " - " + p.Name, "不明な状態: " + item.GetAttributeValue("class"), LogColor.Error); break;
                }

#if !NOCOMMENT
                //メッセージ取得　タグの中身があるかチェック
                tmp1 = item.Find("div", "class", "pf_msg", true)[0];
                if (tmp1.Items.Count > 0) {
                    string ttt = tmp1.Items[0].Text;
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
            return new FormFlashCaribbeancomgirl(performer);
        }

        public string GetFlashUrl(Performer performer) {
            //2010/04/25 最初JSONファイルを読み込んでIDを探す。IDがない場合だけプロフを読むよう変更（負荷対策？）
            //FlashのURLを返す・・JSONファイルまたは待機画像ページのHTMLから取得する
//https://www.caribbeancomgirl.com/flash/chat/limited_preview.swf?channel=76485149&from_site=1003332&performer_id=76485149
//&performer_name=Myakporno&session_type=115&vw_count=1&lang_id=jp&userSiteID=&skinName=&user_type=
//&copyright=(C)2004 Caribbeancomgirl.com ALL RIGHTS RESERVED.
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
                        sFlash = TopPageUrl + "flash/chat/limited_preview.swf?"; //freePreview20.swf | limitedFreePreview20.swf
                        sFlash += "channel=" + m.Groups[1].Value;
                        sFlash += "&from_site=1003332&performer_id=" + m.Groups[1].Value;
                        sFlash += "&performer_name=" + performer.Name;
                        sFlash += "&session_type=" + m.Groups[2].Value;
                        sFlash += "&vw_count=" + m.Groups[4].Value + "&lang_id=jp&userSiteID=&skinName=&user_type=";
                        sFlash += "&copyright=(C)2004 Caribbeancomgirl.com ALL RIGHTS RESERVED.";
                        */
                    } else {
                        //JSONファイルにないのでプロフからURL取得
                        Log.Add(Site + " - " + performer.Name, "JSONにIDなし", LogColor.Error);
                        sHtml = wc.DownloadString(TopPageUrl + "preview/" + performer.Name);
                        if (RegexGetSwf.Match(sHtml).Groups[1].Value != null) {
                            //sFlash = TopPageUrl + "flash/chat/freePreview20.swf?"; //freePreview20.swf | limitedFreePreview20.swf
                            sFlash = TopPageUrl + "flash/chat/limited_preview.swf?"; //freePreview20.swf | limitedFreePreview20.swf
                            sFlash += RegexGetSwf.Match(sHtml).Groups[1].Value;
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
