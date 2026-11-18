"""Approximate China centre hints (T293.5): id, longitude, latitude.

Hints, not evidence: resolve_sites.py matches each hint against the pinned Pleiades archive
(name keyword AND within 0.6 degrees). The archive's East Asia coverage is sparse, so most
centres are expected to stay author geographic hints (regional=true) with labelled contextual
ranges in sources/chronology.json - the Madaktu pattern India used for South Asia.
"""
T = """xianyang 108.71 34.34|yong 107.40 34.52|yueyang 109.30 34.66|du 108.94 34.20|
chencang 107.15 34.38|hangu 110.31 34.55|wuguan 110.26 33.71|shangyi 110.65 33.51|
nanzheng 107.03 33.06|didao 103.86 35.34|anyi 110.51 34.92|daliang 114.31 34.79|
yewang 112.90 35.25|ye 114.40 36.10|quwo 111.50 35.62|xingyang 113.39 34.80|
yanling 114.15 34.10|chaoge 113.92 35.61|xinzheng 113.73 34.40|yiyang 112.10 34.52|
changzi 112.88 36.20|pingyang 111.50 36.08|handan 114.49 36.61|jinyang 112.42 37.85|
dai 114.15 39.85|yuci 112.72 37.70|hejian 116.10 38.45|jingxing 114.15 38.03|
yanmen 112.85 39.30|yunzhong 111.15 40.55|jiuyuan 109.95 40.58|wu'an 114.19 36.75|
linzi 118.06 36.80|jimo 120.35 36.58|ju 118.83 35.58|gaomi 119.75 36.38|
langya 119.90 35.70|xue 117.42 34.93|tao 115.53 35.08|ji 116.35 39.90|
xiadu 115.75 39.35|xiangping 123.17 41.27|juyong 116.08 40.29|shanggu 115.50 40.40|
yuyang 117.15 40.40|jiangling 112.20 30.35|chen 114.88 33.73|shouchun 116.78 32.57|
kuaiji 120.62 31.30|wan 112.52 33.00|pengcheng 117.18 34.26|qianzhong 109.60 28.30|
zhongli 117.80 32.90|xiapi 118.30 34.30|fan 112.14 32.05|deng 112.40 32.20|
shangcai 114.48 33.38|guangling 119.42 32.39|pei 116.92 34.72|e 114.87 30.39|
luoyang 112.45 34.62|gong 112.98 34.75|chenggao 113.40 34.88|mengjin 112.42 34.85|
shangqiu 115.65 34.41|puyang 115.03 35.71|lingshou 114.58 38.35|quyang 114.70 38.62|
qufu 116.99 35.60|shanyin 120.55 30.01|chengdu 104.07 30.67|pix 103.90 30.80|
linqiong 103.46 30.41|jiangzhou 106.55 29.56|zhi 107.40 29.70|yufu 109.57 31.03|
longcheng 107.60 41.60|donghu_court 118.20 43.30|yuezhi_court 100.20 38.30|
"""
