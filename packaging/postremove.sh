#!/bin/sh
# rpm 的 %postun 脚本：fpm 生成的 rpm 文件清单只含文件、不含目录项，
# 卸载后 /opt/todolist 会残留一个空目录，这里补一次清理。
# 目录非空（例如用户自己往里放了文件）时 rmdir 直接失败即止，绝不递归删除任何内容。
rmdir /opt/todolist 2>/dev/null || :