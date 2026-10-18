package pt.yaku.booru.danbooru;

import lombok.Getter;
import lombok.NoArgsConstructor;
import lombok.Setter;

@Getter
@Setter
@NoArgsConstructor
public class DanbooruTag {
    private long id;
    private String name;
    private long postCount;
}
