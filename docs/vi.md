# Editor Tools — dựng cảnh và kịch bản ngay trong trò chơi đang chạy

Editor Tools là những cửa sổ dành cho tác giả mở trên trò chơi đang chạy: trình sửa cảnh, trình sửa kịch bản, trình sửa bản đồ thế giới, trình sửa vật tương tác và ánh sáng, trình dựng cảnh Godot và phần xuất danh mục của mod. Mọi thứ chúng lưu đều là tệp mod bình thường, và trò chơi nhận thay đổi ngay lập tức, nên bạn viết ra một địa điểm, bước vào đó và chơi luôn mà không phải khởi động lại.

Mod này cũng chứa phần nền của trình sửa vật phẩm và nhân vật (bản thân chúng là mod riêng: Items Editor và Characters Editor). Một trò chơi nhận các công cụ này qua bộ Developer Tools hoặc qua bộ sưu tập riêng của nó.

## Mở các công cụ

Trong game, nút chiếc búa trên thanh công cụ mở hộp công cụ:

![Thanh công cụ của mẫu tiểu thuyết với chiếc búa đang được nhấn và hộp công cụ mở ra: Interactive Editor, Light Editor, Map Editor, Scene editor.](assets/toolbox.jpg)

- **Trình sửa cảnh**, **trình sửa bản đồ**, **trình sửa tương tác** và **trình sửa ánh sáng** nằm trong hộp công cụ. Các mod công cụ khác thêm mục của riêng chúng vào đó (trình sửa vật phẩm, nhân vật và bản đồ RPG, trình sửa dữ liệu).
- **Trình sửa kịch bản** mở từ trình sửa cảnh: chọn một cảnh, chọn một kịch bản của cảnh rồi nhấn **Mở**, hoặc **+ Kịch bản mới**.
- Nếu các công cụ được tải ở menu chính (ví dụ với `--all-mods`), mỗi công cụ cũng có nút trong khối công cụ của menu, kể cả trình dựng cảnh và phần xuất danh mục.

Các cửa sổ vẫn mở khi chuyển cảnh, có thể di chuyển và đổi kích thước, và đóng bằng dấu ×.

## Một chỉnh sửa nhỏ trọn vẹn: một địa điểm mới

Mẫu tiểu thuyết có một thị trấn, một quán bar và một ngôi nhà. Ở đây ta thêm một công viên đi được từ thị trấn, chỉ bằng các trình sửa.

**1. Một cảnh mới.** Trong trình sửa cảnh nhấn **+ Cảnh mới**, gõ tên, chọn mod để lưu và thêm phông nền:

![Trình sửa cảnh: bên trái là các cảnh my_novel_bar, my_novel_home và my_novel_town của my_novel_game; ở giữa là phông nền home/vestibule đã thêm vào cảnh; bên phải là Tên my_novel_park, Lưu vào my_novel_game, Âm thanh không, Kịch bản mặc định không.](assets/new-scene.jpg)

Lưu (Ctrl+S) ghi `scenes/my_novel_park.json` vào `my_novel_game`; trò chơi biết cảnh này ngay.

**2. Kịch bản của cảnh.** **+ Kịch bản mới** mở trình sửa kịch bản với một tệp cho cảnh. Công viên chỉ có một lối về:

![Trình sửa kịch bản với my_novel_park.scenario: cảnh my_novel_park, ưu tiên 10, lời dẫn "Một công viên yên tĩnh sau thị trấn. Lá ướt, chiếc ghế trống.", lựa chọn "Làm gì tiếp?" với "Về thị trấn" dẫn tới switch_scene my_novel_town.](assets/park-scenario.jpg)

**3. Một lối đến đó.** Trong kịch bản của thị trấn, thêm một câu trả lời và một nhãn:

![Trình sửa kịch bản với my_novel_town.scenario: lựa chọn "Đi đâu?" giờ có câu trả lời "Ra công viên" -> :park, và nhãn :park thực hiện switch_scene my_novel_park.](assets/town-scenario.jpg)

```scenario
option "Ra công viên" -> :park

:park
switch_scene my_novel_park
end
```

Lưu kịch bản sẽ xóa bộ nhớ đệm kịch bản của game, nên lần vào thị trấn tiếp theo sẽ chơi đoạn văn bản mới.

**4. Chơi thử.** **Đến cảnh** trong trình sửa cảnh, hoặc cứ chơi tiếp, sẽ vào thị trấn, và câu trả lời mới đã ở đó:

![Thị trấn trong game đang chạy: "Đi đâu?" với Vào quán bar, Ra công viên, Về nhà.](assets/town-choice.jpg)

![Công viên mới sau khi chọn Ra công viên: "Làm gì tiếp?" với Về thị trấn.](assets/park.jpg)

## Các trình sửa

- **Trình sửa cảnh** — các địa điểm của tiểu thuyết: tên, mod để lưu, phông nền (mỗi lần vào chọn ngẫu nhiên một), âm thanh nền có nút nghe thử, kịch bản mặc định và các kịch bản của cảnh. **Đến cảnh** kết thúc đoạn hội thoại đang chạy và vào cảnh.
- **Trình sửa kịch bản** — tệp của mọi mod, bản tóm tắt và đồ thị cảnh cùng chuyển cảnh; tô sáng DSL; Ctrl+S để lưu; **Chơi** (F5) lưu và phát kịch bản ngay trong game đang chạy, còn Dừng đưa game về chỗ cũ; **Mở bằng trình sửa ngoài** mở tệp bằng chương trình mặc định của hệ thống.
- **Trình sửa bản đồ** — các vùng của bản đồ thế giới.
- **Trình sửa tương tác** — những vật bấm được của cảnh hiện tại.
- **Trình sửa ánh sáng** — đèn của cảnh hiện tại.
- **Trình dựng cảnh** — cảnh `.tscn` của Godot cho các mod.
- **Xuất danh mục** — đóng gói mod thành zip và ghi mục danh mục kèm tổng kiểm tra.

Hoàn tác hoạt động trong khi sửa (Ctrl+Z trong phần văn bản và trong biểu mẫu của trình sửa vật phẩm, nhân vật). Kiểm tra mod có tham chiếu hỏng là việc của Validator; các trình sửa không cản bạn lưu một kịch bản còn lỗi.

## Tệp của ai được ghi

Các công cụ chỉ ghi vào mod được cài dưới dạng thư mục. Một cảnh là tổng các tệp `scenes/<name>.json` của mọi mod, nên trình sửa cảnh ghi tệp của mod được chọn trong **Lưu vào**. Khi sửa một cảnh thuộc mod khác hoặc mod đã đóng gói, một tệp bổ sung cho nó sẽ được tạo trong mod của bạn; những phông nền đến từ tệp khác hiện ở trạng thái khóa. Kịch bản được lưu vào mod mà chúng thuộc về.

## Khi có trục trặc

1. **Không thấy nút chiếc búa.** Trò chơi không có công cụ; hãy thêm bộ Developer Tools vào bộ sưu tập của nó hoặc chạy với `--with-mods=editor_tools`.
2. **Kịch bản mới không phát.** Phần đầu của nó ghi cảnh khác, hoặc một kịch bản khác của cảnh được chọn trước (so sánh `priority` của chúng).
3. **Mục Lưu vào không có mod nào.** Chỉ ghi được vào mod cài dưới dạng thư mục.
