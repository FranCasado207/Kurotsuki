package pt.yaku.client;

import lombok.Getter;
import lombok.NoArgsConstructor;
import lombok.Setter;

@Getter
@Setter
@NoArgsConstructor
public class GelbooruTag {
    private long id;
    private String name;
    private long count;
    private int type;
    private int ambiguous;
}
