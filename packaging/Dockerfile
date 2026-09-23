# Linux 安装包打包工具链：fpm 统一产出 deb / rpm / apk。
# - rpm 需要 rpmbuild（Debian 的 rpm 包），cpio 用于 rpm 载荷；
# - binutils 提供 ar，fpm 生成 deb 时必需；
# - fpm 版本固定，保证包元数据与体积可复现；升级需同步修改此文件（打包工具链的唯一权威源）。
FROM ruby:3.3-slim

RUN apt-get update \
 && apt-get install -y --no-install-recommends rpm cpio binutils gzip tar xz-utils \
 && rm -rf /var/lib/apt/lists/* \
 && gem install fpm -v 1.18.0 --no-document

WORKDIR /work