`japan-holidays.csv` 是内阁府原始 Shift-JIS CSV，2026-10-20 下载，覆盖 1955–2027 年。

- CSV：https://www8.cao.go.jp/chosei/shukujitsu/syukujitsu.csv
- 说明：https://www8.cao.go.jp/chosei/shukujitsu/gaiyou.html

包含国民の祝日、振替休日和国民の休日。原始文件不作手工修改；服务每周自动刷新，本地副本用于首次运行及离线回退。2028 年及以后须等待并自动取得内阁府发布的对应年份数据。某年份数据缺失时不会将它误判为无假日。

天气来自 Open-Meteo：https://open-meteo.com/ ，地名搜索来自 Open-Meteo Geocoding / GeoNames：https://www.geonames.org/ 。
天气数据按 CC BY 4.0 使用并署名，非商业免费 API 的使用范围以 Open-Meteo 当前条款为准。程序仅向对应服务发送地名搜索词、所选经纬度和时区，不上传个人设置文件。

时区映射使用 Unicode CLDR 的 `windowsZones.xml`，用于将天气 API 的 IANA 时区对应到 Windows 时区；来源：https://github.com/unicode-org/cldr/blob/main/common/supplemental/windowsZones.xml 。该数据编入核心 DLL，许可见 `UNICODE-LICENSE.txt`。

日本地名搜索来自国土地理院「地理院地図」的地址搜索功能：https://maps.gsi.go.jp/ 。只在用户点击搜索时请求，并缓存结果；外部功能的可用性与接口可能变化。使用说明：https://github.com/gsi-cyberjapan/gsimaps/issues/29 。全球地名仍来自 Open-Meteo / GeoNames。
