#define ACHA //アダルト
//#define MACHA //マダム
//#define WACHA //ワールド
//#define OCHA //おしゃべり
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
using System.Globalization;
using LiveViewer;
using LiveViewer.Tool;
using LiveViewer.HtmlParser;

#if ACHA //アダルト
namespace Plugin_DMMa {
    public class Plugin_DMMa : ISitePlugin {
#elif MACHA //マダム
namespace Plugin_DMMm {
    public class Plugin_DMMm : ISitePlugin {
#elif WACHA //ワールド
namespace Plugin_DMMw {
    public class Plugin_DMMw : ISitePlugin {
#else //おしゃべり
namespace Plugin_DMMo {
    public class Plugin_DMMo : ISitePlugin {
#endif

        #region ■定義

        private class FormFlashDMM : FormFlash {
            public FormFlashDMM(Performer pef)
                : base(pef) {
                this.ClientSize = new Size(640, 480);
                this.FormBorderStyle = FormBorderStyle.FixedSingle; //固定サイズ
                //this.SizeChanged += FormFlashDMM_SizeChanged;
            }
        }

        private class UTFstr {
            private string enc;

            public UTFstr(string encStr) {
                enc = encStr;
            }

            public string Decode() {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < enc.Length; i++) {
                    if (enc[i] != '\\') {
                        sb.Append(enc[i]);
                    } else {
                        Match match = Regex.Match(enc.Substring(i), @"\\u00([0-9a-f]{2})", RegexOptions.Compiled);
                        if (match.Success) {
                            int ch = Convert.ToInt32(match.Groups[1].Value, 16);
                            sb.Append((Char)ch);
                            i += 5;
                        }
                    }
                }
                return sb.ToString();
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

        private Parser    Parser   = new Parser();    //HTML解析用パーサ

        //HTML解析用の正規表現
        private Regex RegexGetPef  = new Regex("(listbox +|fl-)(.*)", RegexOptions.Compiled);
        private Regex RegexGetId   = new Regex("/character_id=([0-9]*)/", RegexOptions.Compiled);
        private Regex RegexGetView = new Regex("viewer(| full)", RegexOptions.Compiled);
        private Regex RegexGetEv   = new Regex("event=([0-9]*)", RegexOptions.Compiled); //Event
        private Regex RegexGetRoom = new Regex("/([a-zA-Z0-9]*)/?\\Z", RegexOptions.Compiled); //Event
        private Regex RegexGetSts  = new Regex("st-(.*)", RegexOptions.Compiled); //Event

        private Regex RegexGetSwf1 = new Regex("var[ \\t]+vars[ \\t]*=[ \\t\\n]*\"([^\"]*)\";", RegexOptions.Compiled);
        private Regex RegexGetSwf2 = new Regex("var[ \\t]+url[ \\t]*=[ \\t]*\"([^\"]*)\";", RegexOptions.Compiled);
        private Regex RegexGetSwf3 = new Regex("var[ \\t]+status[ \\t]*=[ \\t]*\"([^\"]*)\";", RegexOptions.Compiled);
        private Regex RegexGetSwf4 = new Regex("var[ \\t]+swf_mod_date[ \\t]*=[ \\t]*\"?([0-9]*)\"?;", RegexOptions.Compiled);
        private Regex RegexGetSwf5 = new Regex("var[ \\t]+swf_stamp[ \\t]*=[ \\t]*\"?([0-9]*)\"?;", RegexOptions.Compiled); //Event

        //オンライン・イベントリストURL
        private string[] sUrlList  = new string[] {"ajax-refresh/=/group=%%ROOMNAME%%/sort=rand/type=all/",
                                                   "ajax-top-event/=/group=%%ROOMNAME%%/"};

#if ACHA //アダルト
        private readonly string sRoomName = "acha";
#elif MACHA //マダム
        private readonly string sRoomName = "macha";
#elif WACHA //ワールド
        private readonly string sRoomName = "wacha";
#else //おしゃべり
        private readonly string sRoomName = "ocha";
#endif
        #endregion


        #region ■ISitePluginインターフェースの実装

#if FIX_IMPORT
        public string Site       { get { return "DMM" + sRoomName[0]; } }
#else
        public string Site       { get { return "DMM(" + sRoomName[0] + ")"; } }
#endif
        public string Caption    { get { return Site + "用のプラグイン(2019/04/30版)"; } }

        public string TopPageUrl { get { return "https://www.dmm.co.jp/live/chat/"; } }

        public void Begin() {
            //プラグイン開始時処理
        }

        public void End() {
            //プラグイン終了時処理
        }

        public List<Performer> Update() {
            //オンラインパフォーマーリスト生成
            List<Performer> pefs = new List<Performer>();

            //リストページの読み込みと木構造への変換
            List<HtmlItem> tagTops = new List<HtmlItem>();
            foreach (string sUrl in sUrlList) {
                try {
                    Uri ur = new Uri(TopPageUrl + "-/online-list/"+ sUrl.Replace("%%ROOMNAME%%", sRoomName));
                    Parser.UserAgent = Pub.UserAgent + "_" + Site; //User-Agentを設定
                    Parser.LoadHtml(ur, "UTF-8");
                    Parser.ParseTree();
                    Pub.WebRequestCount++;
                } catch (Exception ex) {
                    //読み込み失敗
                    Log.Add(Site + "-Update失敗", ex.ToString(), LogColor.Error);
                    return null;
                }

                //パフォ情報のタグを取得
                tagTops.AddRange(Parser.Find("li", "class", RegexGetPef));
                Parser.Clear();
            }

            if (Pub.DebugMode == true ) Log.Add(Site, "tagTops.Count: " + tagTops.Count, LogColor.Warning); //DEBUG
            //各パフォの情報を順に処理
            foreach (HtmlItem tagTop in tagTops) {

                //IDと名前の取得
                string sID = RegexGetId.Match(tagTop.Items[0].GetAttributeValue("href")).Groups[1].Value;
                if (Pub.DebugMode == true )
                    if (pefs.Count < 1) Log.Add(Site, "ID OK", LogColor.Warning); //DEBUG

                //オブジェクト作成
                Performer p = new Performer(this, sID);

                //名前の取得
                HtmlItem tmp1 = tagTop.Find("span", "class", "name", true)[0];
                p.Name = HttpUtilityEx2.HtmlDecode(tmp1.Items[0].Text);

                //状態の取得
                string sStat = null;
                if (RegexGetPef.Match(tagTop.GetAttributeValue("class")).Groups[1].Value == "fl-") { //イベント

                    if (Pub.DebugMode == true) { //DEBUG
                        Log.Add(Site + " - " + p.Name, "Event tagTop: "+tagTop.GetAttributeValue("class"), LogColor.Warning);
                        Log.Add(Site + " - " + p.Name, "Event Status: "+tagTop.Find("span", "class", RegexGetSts, true)[0].GetAttributeValue("class"), LogColor.Warning);
                    }
                    p.RoomName = "event";
                    sStat = tagTop.Find("span", "class", RegexGetSts, true)[0].GetAttributeValue("class");
                    switch (sStat) {
                        case "st-waiting" : p.Dona = false; p.TwoShot = false; break;
                        case "st-party"   : p.Dona = true;  p.TwoShot = false; break;
                        case "st-twoshot" : p.Dona = true;  p.TwoShot = true;  break;
                        case "st-offline" : break;
                        default:Log.Add(Site + " - " + p.Name, "不明な状態 status: " + tagTop.Find("span", "class", RegexGetSts, true)[0].GetAttributeValue("class"), LogColor.Error); break;
                    }
                    //イベントURLにevent=????がある場合はイベントIDを保存
                    if (RegexGetEv.IsMatch(tagTop.Items[0].GetAttributeValue("href")) == true) {
                        p.OtherInfo = "event=" + RegexGetEv.Match(tagTop.Items[0].GetAttributeValue("href")).Groups[1].Value;
                        if (Pub.DebugMode == true) Log.Add(Site + " - " + p.Name, "URL: "+p.OtherInfo, LogColor.Warning); //DEBUG
                    }
                } else { //通常

                    sStat = tagTop.Parent.Parent.GetAttributeValue("class");
                    switch (sStat) {
                        case "waiting-list" : p.Dona = false; p.TwoShot = false; break;
                        case "party-list"   : p.Dona = true;  p.TwoShot = false; break;
                        case "twoshot-list" : p.Dona = true;  p.TwoShot = true;  break;
                        default:Log.Add(Site + " - " + p.Name, "不明な状態 status: " + tagTop.Parent.Parent.GetAttributeValue("class"), LogColor.Error); break;
                    }

                }

#if !NOCOMMENT
                //コメント取得
                if (tagTop.Find("span", "class", "comment", true).Count > 0) {
                    tmp1 = tagTop.Find("span", "class", "comment", true)[0];
                    if (tmp1.Items.Count > 0) {
                        string ttt = tmp1.Items[0].Text;
                        if (Pub.DebugMode)
                            if (HttpUtilityEx2.IsSurrogatePair(ttt))
                                Log.Add(Site + " - " + p.Name, "サロゲートペア文字あり", LogColor.Warning);
                        p.OtherInfo += HttpUtilityEx2.HtmlDecode(ttt);
                    }
                }
#endif

                //人数の取得
                if (tagTop.Find("span", "class", RegexGetView, true).Count > 0) {
                    tmp1 = tagTop.Find("span", "class", RegexGetView, true)[0];
                    if (RegexGetView.Replace(tmp1.GetAttributeValue("class"), "$1") == " full") {
                        p.DonaCount = 99;
                        Log.Add(Site + " - " + p.Name, "status: " + sStat + " persons: full", LogColor.Pef_Change);
                    } else {
                        if (!int.TryParse(tmp1.Items[0].Items[0].Text, out p.DonaCount)) {
                            p.DonaCount = 99;
                            Log.Add(Site + " - " + p.Name, "status: " + sStat + " persons: " + tmp1.Items[0].Items[0].Text, LogColor.Pef_Change);
                        }
                    }
                }

                //デビュー・新人の取得
                if (tagTop.Find("span", "class", "cp-info new-cp", true).Count > 0) {
                    p.Debut = true;
                } else if (tagTop.Find("span", "class", "new", true).Count > 0) {
                    p.NewFace = true;
                }

                //画像URLの取得
                string sImgUrl = tagTop.Find("span", "class", "tmb", true)[0].Items[0].GetAttributeValue("src");
                p.ImageUrl = sImgUrl.Replace("profile_l", "profile");  //小さい画像のサムネ
                p.ImageUpdateCheck = false;

                if (Pub.DebugMode == true )
                    if (pefs.Count < 1) Log.Add(Site, "pefs.Add OK", LogColor.Warning); //DEBUG

                if (sStat != "st-offline")
                    pefs.Add(p);

            }

            return pefs;
        }

        public FormFlash OpenFlash(Performer performer) {
            //小窓を返す
            return new FormFlashDMM(performer);
        }

        public string GetFlashUrl(Performer performer) {
            //FlashのURLを返す・・待機画像ページのHTMLから取得する
            string sFlash = null;
            try {
                using (WebClient wc = new WebClient()) {
                    wc.Headers.Add(HttpRequestHeader.UserAgent, Pub.UserAgent + "_" + Site);
                    wc.Encoding = System.Text.Encoding.UTF8;
                    string sDlUrl = TopPageUrl + "-/chat_room/=/character_id=" + performer.ID + "/";
                    if (performer.RoomName == "event" && performer.OtherInfo != null) //イベントでイベントIDがある場合
                        if (RegexGetEv.IsMatch(performer.OtherInfo) == true)
                            sDlUrl = TopPageUrl + "-/event_room/=/character_id=" + performer.ID + "/" + performer.OtherInfo + "/";
                    string sHtml = wc.DownloadString(sDlUrl);
                    if (RegexGetSwf1.Match(sHtml).Groups[1].Value != null) {
                        UTFstr utfstr1 = new UTFstr(RegexGetSwf1.Match(sHtml).Groups[1].Value);
                        UTFstr utfstr2 = new UTFstr(RegexGetSwf2.Match(sHtml).Groups[1].Value);
                        if (performer.RoomName == "event") {
                            sFlash = utfstr2.Decode() + "event_peep" + ".swf"
                                   + "?" + RegexGetSwf5.Match(sHtml).Groups[1].Value
                                   + "&" + utfstr1.Decode();
                        } else {
                            sFlash = utfstr2.Decode() + RegexGetSwf3.Match(sHtml).Groups[1].Value + ".swf"
                                   + "?" + RegexGetSwf4.Match(sHtml).Groups[1].Value
                                   + "&" + utfstr1.Decode();
                        }
                    }
                    Pub.WebRequestCount++; //GUIの読込回数を増やす
                }
            } catch (Exception ex) {
                Log.Add(Site + "-GetFlashUrl失敗", ex.ToString(), LogColor.Error);
            }
            if (Pub.DebugMode == true)
                if (performer.RoomName == "event") Log.Add(Site + " - " + performer.Name, sFlash, LogColor.Warning); //DEBUG

            return sFlash;
        }

        public Clipping GetFlashClipping(Performer performer) {
            return null;
            /*
            //Flashの切り抜き方法を返す 2017/02/24修正
            Clipping c = new Clipping();
            c.OriginalSize.Width  = 768;  //フラッシュ全体の幅
            c.OriginalSize.Height = 450;  //フラッシュ全体の高さ
            c.ClippingRect.X      = 0;    //切り抜く領域の左上座標(X)
            c.ClippingRect.Y      = 0;    //切り抜く領域の左上座標(Y)
            c.ClippingRect.Width  = 768;  //切り抜く領域の幅
            c.ClippingRect.Height = 576;  //切り抜く領域の高さ
            c.Fixed = true;               //Flashが固定サイズ

            return c;
            */
        }

        public string GetProfileUrl(Performer performer) {
            //プロフィールURLを返す
            string sPUrl = TopPageUrl + "-/chat_room/=/character_id=" + performer.ID + "/";
            if (performer.RoomName == "event" && performer.OtherInfo != null) //イベントでイベントIDがある場合
                if (RegexGetEv.IsMatch(performer.OtherInfo) == true)
                   sPUrl = TopPageUrl + "-/event_room/=/character_id=" + performer.ID + "/" + performer.OtherInfo + "/";

            if (Pub.DebugMode == true)
                if (performer.RoomName == "event") Log.Add(Site + " - " + performer.Name, sPUrl, LogColor.Warning); //DEBUG

            return sPUrl;
        }

        #endregion
    }
}
