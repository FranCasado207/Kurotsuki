package pt.yaku.booru.gelbooru;

import java.util.List;

import jakarta.ws.rs.DefaultValue;
import jakarta.ws.rs.GET;
import jakarta.ws.rs.Path;
import jakarta.ws.rs.QueryParam;
import pt.yaku.booru.dapi.ApiPost;

@Path("/index.php")
public interface GelbooruFamilyApi {

    @GET
    List<ApiPost> listPosts(@QueryParam("page") @DefaultValue("dapi") String page,
                            @QueryParam("s") @DefaultValue("post") String s,
                            @QueryParam("q") @DefaultValue("index") String q,
                            @QueryParam("api_key") String apiKey,
                            @QueryParam("user_id") String userId,
                            @QueryParam("limit") Integer limit,
                            @QueryParam("pid") Integer pid,
                            @QueryParam("tags") String tags,
                            @QueryParam("cid") Long cid,
                            @QueryParam("id") Integer id,
                            @QueryParam("json") @DefaultValue("1") int json);

    @GET
    String listTags(@QueryParam("page") @DefaultValue("dapi") String page,
                    @QueryParam("s") @DefaultValue("tag") String s,
                    @QueryParam("q") @DefaultValue("index") String q,
                    @QueryParam("api_key") String apiKey,
                    @QueryParam("user_id") String userId,
                    @QueryParam("name_pattern") String namePattern,
                    @QueryParam("limit") Integer limit,
                    @QueryParam("orderby") String orderby,
                    @QueryParam("order") String order,
                    @QueryParam("json") @DefaultValue("1") int json);
}
