//#define DEBUG
using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using System.Diagnostics;
using LiveViewer;
using LiveViewer.Tool;
using LiveViewer.HtmlParser;

namespace Plugin_JoshiChat {
    public class Plugin_JoshiChat : ISitePlugin {
        #region ■定義

        private class FormFlashJoshiChat : FormFlash {
            public FormFlashJoshiChat(Performer pef)
                : base(pef) {
            }
        }

        #endregion

        #region ■オブジェクト

        private Parser Parser = new Parser();    //HTML解析用パーサ

        //HTML解析用の正規表現
        private Regex  RegexGetLnk  = new Regex("aid=([0-9]*)", RegexOptions.Compiled);
        private Regex  RegexGetSts  = new Regex("st_([^_]*)(_M)?\\.png", RegexOptions.Compiled);
        private Regex  RegexGetSwf1 = new Regex("'movie','([^']*)'", RegexOptions.Compiled);
        private Regex  RegexGetSwf2 = new Regex("'FlashVars','([^']*)'", RegexOptions.Compiled);

        #endregion


        #region ■ISitePluginインターフェースの実装

        public string Site       { get { return "joshi-tv"; } }

        public string Caption    { get { return "女子校生姉妹チャット用のプラグイン(2014/09/27版)"; } }

        public string TopPageUrl { get { return "http://www.joshi-tv.com/"; } }

        public void Begin() {
            //プラグイン開始時処理
        }

        public void End() {
            //プラグイン終了時処理
        }

        public List<Performer> Update() {
            //オンラインパフォーマーリスト生成
            List<Performer> pefs = new List<Performer>();

            try {
                //トップページの読み込みと木構造への変換
                //User-Agentを設定
                Parser.UserAgent = Pub.UserAgent + "_" + Site;
#if DEBUG
                Parser.LoadHtml(new Uri(TopPageUrl + "main.php?a=n"), "UTF-8");
#else
                Parser.LoadHtml(new Uri(TopPageUrl + "main_online.php"), "UTF-8");
#endif
                Parser.ParseTree();
                Pub.WebRequestCount++;
            } catch (Exception ex) {
                //読み込み失敗
                Log.Add(Site + "-Update失敗", ex.ToString(), LogColor.Error);
                return null;
            }

            //パフォ情報のタグを取得
            List<HtmlItem> tagTops = Parser.Find("div", "class", "table");
            Parser.Clear();
            if (Pub.DebugMode == true ) Log.Add(Site, "tagTops.Count: " + tagTops.Count, LogColor.Warning); //DEBUG

            //各パフォの情報を順に処理
            foreach (HtmlItem tagTop in tagTops) {

                //IDの取得
                HtmlItem tmp1 = tagTop.Find("a", "class", "name", true)[0];
                Match m = RegexGetLnk.Match(tmp1.GetAttributeValue("href"));
                string sID = m.Groups[1].Value;
                if (sID == "") continue;
                if (Pub.DebugMode == true )
                    if (pefs.Count < 1) Log.Add(Site, "ID OK", LogColor.Warning); //DEBUG

                //オブジェクト生成
                Performer p = new Performer(this, sID);

                //名前
                p.Name = tmp1.Items[0].Text;

                //状態
                tmp1 = tagTop.Find("span", "class", "status", true)[0];
                m = RegexGetSts.Match(tmp1.Find("img", "src", true)[0].GetAttributeValue("src"));
                if (m.Groups[2].Value == "_M") p.RoomName = "ﾓﾊﾞｲﾙ";
                switch (m.Groups[1].Value) {
#if DEBUG
                    case "off":   p.Dona = false; p.TwoShot = false; break; //DEBUG
#else
                    case "off":   continue;
                    case "break": continue;
#endif
                    case "wait":  p.Dona = false; p.TwoShot = false; break;
                    case "party": p.Dona = true;  p.TwoShot = false; break;
                    case "2shot": p.Dona = true;  p.TwoShot = true;  break;
                    default: Log.Add(Site + "-ERROR", "不明な状態:" + tmp1.Find("img", "src", true)[0].GetAttributeValue("src"), LogColor.Error); break;
                }

                //画像URLの取得
                tmp1 = tagTop.Find("span", "class", "pic", true)[0];
                string stmp = tmp1.Find("img", "src", true)[0].GetAttributeValue("src");
                if (stmp.LastIndexOf('?') > 0)
                    p.ImageUrl = TopPageUrl + stmp.Substring(0, stmp.IndexOf('?'));
                else
                    p.ImageUrl = TopPageUrl + stmp;
                p.ImageUpdateCheck = false;
                if (Pub.DebugMode == true )
                    if (pefs.Count < 1) Log.Add(Site, p.ImageUrl, LogColor.Warning); //DEBUG

                //コメント
                tmp1 = tagTop.Find("div", "class", "comment", true)[0];
                if (tmp1.Items.Count > 0)
                    p.OtherInfo = HttpUtilityEx.HtmlDecode(tmp1.Items[0].Text);

                if (Pub.DebugMode == true )
                    if (pefs.Count < 1) Log.Add(Site, "pefs.Add OK", LogColor.Warning); //DEBUG

                pefs.Add(p);
            }

            return pefs;
        }

        public FormFlash OpenFlash(Performer performer) {
            //フラッシュ窓を返す・・カスタマイズなし
            return new FormFlashJoshiChat(performer);
        }
        
        public string GetFlashUrl(Performer performer) {
            //FlashのURLを返す
            //http://www.joshi-tv.com/cu/profile.swf?sitename=shimai&deptid=&destid=00001085&lb=&free=8&tran=no::no
            string sFlash = null;
            try {
                using (WebClient wc = new WebClient()) {
                    wc.Headers.Add(HttpRequestHeader.UserAgent, Pub.UserAgent + "_" + Site);
                    string sHtml = wc.DownloadString(TopPageUrl + "cu/profile.php?aid=" + performer.ID);
                    if (RegexGetSwf1.Match(sHtml).Groups[1].Value != null) {
                        sFlash = TopPageUrl + "cu/" + RegexGetSwf1.Match(sHtml).Groups[1].Value + ".swf"
                               + "?" + RegexGetSwf2.Match(sHtml).Groups[1].Value;
                    }
                    Pub.WebRequestCount++; //GUIの読込回数を増やす
                }
            } catch (Exception ex) {
                Log.Add(Site + "-GetFlashUrl失敗", ex.ToString(), LogColor.Error);
            }

            return sFlash;
        }

        public Clipping GetFlashClipping(Performer performer) {
            //Flashの切り抜き方法を返す・・切り抜き不要
            return null;
        }

        public string GetProfileUrl(Performer performer) {
            return TopPageUrl + "cu/profile.php?aid=" + performer.ID;
        }

        #endregion
    }
}
